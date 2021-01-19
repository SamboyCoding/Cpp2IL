using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class CallManagedFunctionAction : BaseAction
    {
        private MethodDefinition? managedMethodBeingCalled;
        private List<IAnalysedOperand>? arguments;
        private ulong _jumpTarget;
        private LocalDefinition? _objectMethodBeingCalledOn;
        private LocalDefinition? _returnedLocal;

        public CallManagedFunctionAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _jumpTarget = instruction.NearBranchTarget;
            _objectMethodBeingCalledOn = context.GetLocalInReg("rcx");
            var listOfCallableMethods = LibCpp2IlMain.GetManagedMethodImplementationsAtAddress(_jumpTarget);

            if (listOfCallableMethods == null) return;

            Il2CppMethodDefinition possibleTarget = null;
            if (listOfCallableMethods.Count == 1)
            {
                possibleTarget = listOfCallableMethods.First();
                if (!MethodUtils.CheckParameters(possibleTarget, context, !possibleTarget.IsStatic, out arguments, failOnLeftoverArgs: false)) 
                    AddComment("parameters do not match, but there is only one function at this address.");
                
                if (!possibleTarget.IsStatic && _objectMethodBeingCalledOn?.Type != null && !Utils.IsManagedTypeAnInstanceOfCppOne(LibCpp2ILUtils.WrapType(possibleTarget.DeclaringType!), _objectMethodBeingCalledOn.Type)) 
                    AddComment($"This is an instance method, but the type of the 'this' parameter is mismatched. Expecting {possibleTarget.DeclaringType.Name}, actually {_objectMethodBeingCalledOn.Type.FullName}");
            }
            else
            {
                //Find the correct method.
                if (_objectMethodBeingCalledOn?.Type != null)
                {
                    //Direct instance methods take priority
                    possibleTarget = listOfCallableMethods.FirstOrDefault(m => !m.IsStatic && Utils.AreManagedAndCppTypesEqual(LibCpp2ILUtils.WrapType(m.DeclaringType!), _objectMethodBeingCalledOn.Type) && MethodUtils.CheckParameters(m, context, true, out arguments));

                    //todo check args and null out

                    if (possibleTarget == null)
                        //Base class instance methods
                        possibleTarget = listOfCallableMethods.FirstOrDefault(m => !m.IsStatic && Utils.IsManagedTypeAnInstanceOfCppOne(LibCpp2ILUtils.WrapType(m.DeclaringType!), _objectMethodBeingCalledOn.Type) && MethodUtils.CheckParameters(m, context, true, out arguments));

                    //check args again.
                }

                //Check static methods
                if (possibleTarget == null)
                    possibleTarget = listOfCallableMethods.FirstOrDefault(m => m.IsStatic && MethodUtils.CheckParameters(m, context, false, out arguments));
            }


            //Resolve unmanaged => managed method.
            if (possibleTarget != null)
                managedMethodBeingCalled = SharedState.UnmanagedToManagedMethods[possibleTarget];

            if (managedMethodBeingCalled?.ReturnType is { } returnType && returnType.FullName != "System.Void")
            {
                if (Utils.TryResolveType(returnType, out var returnDef))
                {
                    //Push return type to rax.
                    var destReg = Utils.ShouldBeInFloatingPointRegister(returnDef) ? "xmm0" : "rax";
                    _returnedLocal = context.MakeLocal(returnDef, reg: destReg);
                }
                else
                {
                    AddComment($"Failed to resolve return type {returnType} for pushing to rax.");
                }
            }

            // SharedState.MethodsByAddress.TryGetValue(jumpTarget, out target);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            string result;
            result = managedMethodBeingCalled?.IsStatic == true 
                ? $"[!] Calls static managed method {managedMethodBeingCalled?.FullName} (0x{_jumpTarget:X})" 
                : $"[!] Calls managed method {managedMethodBeingCalled?.FullName} (0x{_jumpTarget:X}) on instance {_objectMethodBeingCalledOn}";

            if (arguments != null && arguments.Count > 0)
                result += $" with arguments {arguments.ToStringEnumerable()}";
            
            if (_returnedLocal != null)
                result += $" and stores the result in {_returnedLocal} in register rax";

            return result + "\n";
        }
    }
}