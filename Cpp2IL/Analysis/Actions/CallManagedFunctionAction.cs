using System.Collections.Generic;
using System.Linq;
using System.Text;
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


            if (!LibCpp2IlMain.ThePe!.is32Bit)
                _objectMethodBeingCalledOn = context.GetLocalInReg("rcx"); //64-bit, we can get this immediately, no penalty if this is a static method.

            var listOfCallableMethods = LibCpp2IlMain.GetManagedMethodImplementationsAtAddress(_jumpTarget);

            if (listOfCallableMethods == null) return;

            Il2CppMethodDefinition possibleTarget = null;
            if (listOfCallableMethods.Count == 1)
            {
                possibleTarget = listOfCallableMethods.First();

                if (!possibleTarget.IsStatic && LibCpp2IlMain.ThePe!.is32Bit && context.Stack.TryPeek(out var op) && op is LocalDefinition local)
                {
                    _objectMethodBeingCalledOn = local; //32-bit and we have an instance
                    context.Stack.Pop(); //remove instance from stack
                }

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
                    possibleTarget = null;
                    foreach (var m in listOfCallableMethods)
                    {
                        if(m.IsStatic) continue; //Only checking instance methods here.
                        
                        //Have to pop out the instance arg here so we can check the rest of the params, but save it in case we need to push it back.
                        LocalDefinition toPushBackIfNeeded = null;
                        if (LibCpp2IlMain.ThePe!.is32Bit && context.Stack.TryPeek(out var op) && op is LocalDefinition local)
                        {
                            _objectMethodBeingCalledOn = local;
                            toPushBackIfNeeded = local;
                        }

                        //Check defining type matches instance, and check params.
                        if (Utils.AreManagedAndCppTypesEqual(LibCpp2ILUtils.WrapType(m.DeclaringType!), _objectMethodBeingCalledOn.Type) && MethodUtils.CheckParameters(m, context, true, out arguments))
                        {
                            possibleTarget = m;
                            break;
                        }

                        if(toPushBackIfNeeded != null)
                        {
                            //Mismatch - re-push the instance
                            context.Stack.Push(toPushBackIfNeeded);
                        }
                    }

                    //Check for base class instance methods
                    if (possibleTarget == null)
                    {
                        foreach (var m in listOfCallableMethods)
                        {
                            if(m.IsStatic) continue; //Only checking instance methods here.
                        
                            //Again, have to pop out the instance arg here so we can check the rest of the params, but save it in case we need to push it back.
                            LocalDefinition toPushBackIfNeeded = null;
                            if (LibCpp2IlMain.ThePe!.is32Bit && context.Stack.TryPeek(out var op) && op is LocalDefinition local)
                            {
                                _objectMethodBeingCalledOn = local;
                                toPushBackIfNeeded = local;
                            }
                            
                            //Check defining type is a superclass or interface of instance, and check params.
                            if (Utils.IsManagedTypeAnInstanceOfCppOne(LibCpp2ILUtils.WrapType(m.DeclaringType!), _objectMethodBeingCalledOn.Type) && MethodUtils.CheckParameters(m, context, true, out arguments))
                            {
                                possibleTarget = m;
                                break;
                            }

                            if (toPushBackIfNeeded != null)
                            {
                                //mismatch, push back instance
                                context.Stack.Push(toPushBackIfNeeded);
                            }
                        }
                    }
                }

                if (possibleTarget == null)
                    //Check static methods. No need for a complicated foreach here, we don't have to worry about an instance.
                    possibleTarget = listOfCallableMethods.FirstOrDefault(m => m.IsStatic && MethodUtils.CheckParameters(m, context, false, out arguments));
                else if (LibCpp2IlMain.ThePe!.is32Bit && context.Stack.TryPeek(out var op) && op is LocalDefinition local)
                    //Instance method
                    _objectMethodBeingCalledOn = local; //32-bit and we have an instance
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

        private IEnumerable<string> GetReadableArguments()
        {
            foreach (var arg in arguments)
            {
                if (arg is ConstantDefinition constantDefinition)
                    yield return constantDefinition.ToString();
                else
                    yield return ((LocalDefinition) arg).Name;
            }
        }

        public override string? ToPsuedoCode()
        {
            if (managedMethodBeingCalled == null) return "[instruction error - managed method being called is null]";
            
            var ret = new StringBuilder();

            if (_returnedLocal != null)
                ret.Append(_returnedLocal?.Type?.FullName).Append(' ').Append(_returnedLocal?.Name).Append(" = ");

            if (managedMethodBeingCalled.IsStatic)
                ret.Append(managedMethodBeingCalled.DeclaringType.FullName);
            else
                ret.Append(_objectMethodBeingCalledOn?.Name);

            ret.Append('.').Append(managedMethodBeingCalled?.Name).Append('(');

            if (arguments != null && arguments.Count > 0)
                ret.Append(string.Join(", ", GetReadableArguments()));

            ret.Append(')');

            return ret.ToString();
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
        
        public override bool IsImportant()
        {
            return true;
        }
    }
}