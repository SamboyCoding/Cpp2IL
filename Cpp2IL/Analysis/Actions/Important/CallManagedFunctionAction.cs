using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class CallManagedFunctionAction : BaseAction
    {
        internal readonly MethodReference? ManagedMethodBeingCalled;
        private List<IAnalysedOperand>? arguments;
        private readonly ulong _jumpTarget;
        private readonly LocalDefinition? _objectMethodBeingCalledOn;
        private readonly LocalDefinition? _returnedLocal;

        private readonly bool wasArrayInstantiation;
        private readonly long[]? instantiatedArrayValues = null;
        private readonly bool _isSuperMethod;

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

                if (!MethodUtils.CheckParameters(instruction, possibleTarget, context, !possibleTarget.IsStatic, out arguments, _objectMethodBeingCalledOn, failOnLeftoverArgs: false))
                    AddComment("parameters do not match, but there is only one function at this address.");

                if (!possibleTarget.IsStatic && _objectMethodBeingCalledOn?.Type != null && !Utils.IsManagedTypeAnInstanceOfCppOne(LibCpp2ILUtils.WrapType(possibleTarget.DeclaringType!), _objectMethodBeingCalledOn.Type))
                    AddComment($"This is an instance method, but the type of the 'this' parameter is mismatched. Expecting {possibleTarget.DeclaringType.Name}, actually {_objectMethodBeingCalledOn.Type.FullName}");
                else if (!possibleTarget.IsStatic && _objectMethodBeingCalledOn?.Type != null)
                {
                    //Matching type, but is it us or a base type?
                    _isSuperMethod = !Utils.AreManagedAndCppTypesEqual(LibCpp2ILUtils.WrapType(possibleTarget.DeclaringType!), _objectMethodBeingCalledOn.Type);
                }
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
                        if (m.IsStatic) continue; //Only checking instance methods here.

                        //Have to pop out the instance arg here so we can check the rest of the params, but save it in case we need to push it back.
                        LocalDefinition toPushBackIfNeeded = null;
                        if (LibCpp2IlMain.ThePe!.is32Bit && context.Stack.TryPeek(out var op) && op is LocalDefinition local)
                        {
                            _objectMethodBeingCalledOn = local;
                            toPushBackIfNeeded = local;
                        }

                        //Check defining type matches instance, and check params.
                        if (Utils.AreManagedAndCppTypesEqual(LibCpp2ILUtils.WrapType(m.DeclaringType!), _objectMethodBeingCalledOn.Type))
                        {
                            possibleTarget = m;

                            if (!MethodUtils.CheckParameters(instruction, m, context, true, out arguments, _objectMethodBeingCalledOn))
                                AddComment("parameters do not match, but declaring type of method matches instance.");

                            break;
                        }

                        if (toPushBackIfNeeded != null)
                        {
                            //Mismatch - re-push the instance
                            context.Stack.Push(toPushBackIfNeeded);
                        }
                    }

                    //Check for base class instance methods
                    if (possibleTarget == null)
                    {
                        LocalDefinition toPushBackIfNeeded = null;
                        if (LibCpp2IlMain.ThePe!.is32Bit && context.Stack.TryPeek(out var op) && op is LocalDefinition local)
                        {
                            _objectMethodBeingCalledOn = local;
                            toPushBackIfNeeded = local;
                        }

                        //Methods which are non-static and for which the declaring type is some form of supertype of the object we're calling on.
                        var baseClassMethods = listOfCallableMethods.Where(m => !m.IsStatic && Utils.IsManagedTypeAnInstanceOfCppOne(LibCpp2ILUtils.WrapType(m.DeclaringType!), _objectMethodBeingCalledOn.Type)).ToList();
                        
                        if (baseClassMethods.Count == 0 && toPushBackIfNeeded != null)
                        {
                            //mismatch, push back instance
                            context.Stack.Push(toPushBackIfNeeded);
                        }

                        if (baseClassMethods.Count == 1)
                        {
                            possibleTarget = baseClassMethods.Single();
                            _isSuperMethod = true;
                            
                            //Only one method - we can be less strict on leftover parameters.
                            if(!MethodUtils.CheckParameters(instruction, baseClassMethods.Single(), context, true, out arguments, _objectMethodBeingCalledOn, false))
                                AddComment("Parameter mismatch, but there is only one method here for which the instance matches.");
                        }
                        else
                        {
                            foreach (var m in baseClassMethods)
                            {
                                //Check params, and be strict about it (no leftover arguments).
                                if (MethodUtils.CheckParameters(instruction, m, context, true, out arguments, _objectMethodBeingCalledOn))
                                {
                                    possibleTarget = m;
                                    _isSuperMethod = true;
                                    break;
                                }
                            }

                            if (possibleTarget == null)
                            {
                                //Iterate again, and accept (but warn) leftover arguments.
                                
                                //Sort by number of parameters, descending, so we don't pick up a no-arg function by mistake.
                                baseClassMethods.Sort((a, b) => a.parameterCount - b.parameterCount);
                                foreach (var m in baseClassMethods)
                                {
                                    //Check params, and be strict about it (no leftover arguments).
                                    if (MethodUtils.CheckParameters(instruction, m, context, true, out arguments, _objectMethodBeingCalledOn, false))
                                    {
                                        possibleTarget = m;
                                        _isSuperMethod = true;
                                        AddComment("Leftover parameters detected, but first few match.");
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (possibleTarget == null)
                    //Check static methods. No need for a complicated foreach here, we don't have to worry about an instance.
                    possibleTarget = listOfCallableMethods.FirstOrDefault(m => m.IsStatic && MethodUtils.CheckParameters(instruction, m, context, false, out arguments, _objectMethodBeingCalledOn));
                else if (LibCpp2IlMain.ThePe!.is32Bit && context.Stack.TryPeek(out var op) && op is LocalDefinition local)
                    //Instance method
                    _objectMethodBeingCalledOn = local; //32-bit and we have an instance
            }

            if (possibleTarget == null && LibCpp2IlMain.ThePe.ConcreteGenericImplementationsByAddress.TryGetValue(_jumpTarget, out var concreteGenericMethods))
            {
                AddComment($"Probably a jump to a concrete generic method, there are {concreteGenericMethods.Count} here.");
            }

            if (possibleTarget == null && _objectMethodBeingCalledOn?.Type != null)
            {
                var il2cppType = SharedState.ManagedToUnmanagedTypes[_objectMethodBeingCalledOn.Type.Resolve()];
                var rgctxs = il2cppType.RGCTXs;
                if (rgctxs.Length > 0)
                {
                    AddComment("Found at least one RGCTX");
                }
            }


            //Resolve unmanaged => managed method.
            if (possibleTarget != null)
                ManagedMethodBeingCalled = SharedState.UnmanagedToManagedMethods[possibleTarget];
            else
                AddComment($"Failed to resolve any matching method (there are {listOfCallableMethods.Count} at this address)");

            if (ManagedMethodBeingCalled?.ReturnType is { } returnType && returnType.FullName != "System.Void")
            {
                if (returnType is GenericParameter gp && _objectMethodBeingCalledOn?.Type != null)
                {
                    returnType = MethodUtils.ResolveGenericParameterType(ManagedMethodBeingCalled, _objectMethodBeingCalledOn.Type, gp);
                }

                if (returnType is GenericInstanceType git)
                {
                    try
                    {
                        returnType = MethodUtils.ResolveMethodGIT(git, ManagedMethodBeingCalled, _objectMethodBeingCalledOn?.Type, arguments?.Select(a => a is LocalDefinition l ? l.Type : null).ToArray() ?? new TypeReference[0]);
                    }
                    catch (Exception e)
                    {
                        AddComment("Failed to resolve return type generic arguments.");
                    }
                }

                var destReg = Utils.ShouldBeInFloatingPointRegister(returnType) ? "xmm0" : "rax";
                _returnedLocal = context.MakeLocal(returnType, reg: destReg);
                
                //todo maybe improve?
                RegisterUsedLocal(_returnedLocal);
            }

            if (ManagedMethodBeingCalled?.FullName == "System.Void System.Runtime.CompilerServices.RuntimeHelpers::InitializeArray(System.Array,System.RuntimeFieldHandle)")
            {
                if (arguments?.Count > 1 && arguments[1] is ConstantDefinition {Value: FieldDefinition fieldDefinition} && arguments[0] is LocalDefinition {KnownInitialValue: AllocatedArray arr})
                {
                    instantiatedArrayValues = Utils.ReadArrayInitializerForFieldDefinition(fieldDefinition, arr);
                    wasArrayInstantiation = true;
                    AddComment("Initializes array containing values: " + instantiatedArrayValues.ToStringEnumerable());
                }
            }

            arguments?.Where(o => o is LocalDefinition).ToList().ForEach(o => RegisterUsedLocal((LocalDefinition) o));
            if(_objectMethodBeingCalledOn != null)
                RegisterUsedLocal(_objectMethodBeingCalledOn);

            // SharedState.MethodsByAddress.TryGetValue(jumpTarget, out target);
        }

        public List<Mono.Cecil.Cil.Instruction> GetILToLoadParams(MethodAnalysis context, ILProcessor processor, bool includeThis = true)
        {
            if (ManagedMethodBeingCalled == null || arguments == null || arguments.Count != ManagedMethodBeingCalled.Parameters.Count)
                throw new TaintedInstructionException();

            var result = new List<Mono.Cecil.Cil.Instruction>();

            if (ManagedMethodBeingCalled.HasThis && includeThis)
                result.Add(context.GetILToLoad(_objectMethodBeingCalledOn ?? throw new TaintedInstructionException(), processor));

            foreach (var operand in arguments)
            {
                if (operand is LocalDefinition l)
                    result.Add(context.GetILToLoad(l, processor));
                else if (operand is ConstantDefinition c)
                    result.AddRange(c.GetILToLoad(context, processor));
                else
                    throw new TaintedInstructionException();
            }

            return result;
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            if (ManagedMethodBeingCalled?.Name == ".ctor")
            {
                //todo check if calling alternative constructor
                if (!(_isSuperMethod && _objectMethodBeingCalledOn?.Name == "this"))
                    return Array.Empty<Mono.Cecil.Cil.Instruction>(); //Ignore ctors that aren't super calls, because we're allocating a new instance.
            }
            
            var result = GetILToLoadParams(context, processor);

            //todo support callvirt
            result.Add(processor.Create(OpCodes.Call, ManagedMethodBeingCalled));

            if (ManagedMethodBeingCalled.ReturnType.FullName == "System.Void")
                return result.ToArray();

            if (_returnedLocal == null)
                throw new TaintedInstructionException();

            result.Add(processor.Create(OpCodes.Stloc, _returnedLocal.Variable));

            return result.ToArray();
        }

        private IEnumerable<string> GetReadableArguments()
        {
            foreach (var arg in arguments)
            {
                if (arg is ConstantDefinition constantDefinition)
                    yield return constantDefinition.ToString();
                else
                    yield return (arg as LocalDefinition)?.Name ?? "null";
            }
        }

        public override string? ToPsuedoCode()
        {
            if (ManagedMethodBeingCalled == null) return "[instruction error - managed method being called is null]";

            if (wasArrayInstantiation)
            {
                var arrayType = ((ArrayType) ((LocalDefinition) arguments[0]).Type).ElementType;
                return $"{arguments![0].GetPseudocodeRepresentation()} = new {arrayType}[] {{{string.Join(", ", instantiatedArrayValues!)}}}";
            }

            var ret = new StringBuilder();

            if (_returnedLocal != null)
                ret.Append(_returnedLocal?.Type?.FullName).Append(' ').Append(_returnedLocal?.Name).Append(" = ");

            if (!ManagedMethodBeingCalled.HasThis)
                ret.Append(ManagedMethodBeingCalled.DeclaringType.FullName);
            else if (_objectMethodBeingCalledOn?.Name == "this")
                ret.Append(_isSuperMethod ? "base" : "this");
            else
                ret.Append(_objectMethodBeingCalledOn?.Name);

            ret.Append('.').Append(ManagedMethodBeingCalled?.Name).Append('(');

            if (arguments != null && arguments.Count > 0)
                ret.Append(string.Join(", ", GetReadableArguments()));

            ret.Append(')');

            return ret.ToString();
        }

        public override string ToTextSummary()
        {
            string result;
            result = ManagedMethodBeingCalled?.HasThis == false
                ? $"[!] Calls static managed method {ManagedMethodBeingCalled?.FullName} (0x{_jumpTarget:X})"
                : $"[!] Calls managed method {ManagedMethodBeingCalled?.FullName} (0x{_jumpTarget:X}) on instance {_objectMethodBeingCalledOn}";

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

        public override bool PseudocodeNeedsLinebreakBefore()
        {
            return true;
        }
    }
}