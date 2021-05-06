using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class CallManagedFunctionAction : AbstractCallAction
    {
        private readonly ulong _jumpTarget;

        private readonly bool wasArrayInstantiation;
        private readonly long[]? instantiatedArrayValues = null;

        public CallManagedFunctionAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _jumpTarget = instruction.NearBranchTarget;

            if (!LibCpp2IlMain.Binary!.is32Bit)
                InstanceBeingCalledOn = context.GetLocalInReg("rcx"); //64-bit, we can get this immediately, no penalty if this is a static method.

            var listOfCallableMethods = LibCpp2IlMain.GetManagedMethodImplementationsAtAddress(_jumpTarget);

            if (listOfCallableMethods == null)
            {
                if(LibCpp2IlMain.Binary!.ConcreteGenericImplementationsByAddress.TryGetValue(_jumpTarget, out var concMethods))
                    AddComment($"There are {concMethods.Count} concrete generic implementation(s) at this address");
                
                return;
            }

            Il2CppMethodDefinition possibleTarget = null;
            if (listOfCallableMethods.Count == 1)
            {
                possibleTarget = listOfCallableMethods.First();

                if (!possibleTarget.IsStatic && LibCpp2IlMain.Binary!.is32Bit && context.Stack.TryPeek(out var op) && op is LocalDefinition local)
                {
                    InstanceBeingCalledOn = local; //32-bit and we have an instance
                    context.Stack.Pop(); //remove instance from stack
                }

                if (!MethodUtils.CheckParameters(instruction, possibleTarget, context, !possibleTarget.IsStatic, out Arguments, InstanceBeingCalledOn, failOnLeftoverArgs: false))
                    AddComment("parameters do not match, but there is only one function at this address.");

                if (!possibleTarget.IsStatic && InstanceBeingCalledOn?.Type != null && !Utils.IsManagedTypeAnInstanceOfCppOne(LibCpp2ILUtils.WrapType(possibleTarget.DeclaringType!), InstanceBeingCalledOn.Type))
                    AddComment($"This is an instance method, but the type of the 'this' parameter is mismatched. Expecting {possibleTarget.DeclaringType.Name}, actually {InstanceBeingCalledOn.Type.FullName}");
                else if (!possibleTarget.IsStatic && InstanceBeingCalledOn?.Type != null)
                {
                    //Matching type, but is it us or a base type?
                    IsCallToSuperclassMethod = !Utils.AreManagedAndCppTypesEqual(LibCpp2ILUtils.WrapType(possibleTarget.DeclaringType!), InstanceBeingCalledOn.Type);
                }
            }
            else
            {
                //Find the correct method.
                if (InstanceBeingCalledOn?.Type != null)
                {
                    //Direct instance methods take priority
                    possibleTarget = null;
                    foreach (var m in listOfCallableMethods)
                    {
                        if (m.IsStatic) continue; //Only checking instance methods here.

                        //Have to pop out the instance arg here so we can check the rest of the params, but save it in case we need to push it back.
                        LocalDefinition toPushBackIfNeeded = null;
                        if (LibCpp2IlMain.Binary!.is32Bit && context.Stack.TryPeek(out var op) && op is LocalDefinition local)
                        {
                            InstanceBeingCalledOn = local;
                            toPushBackIfNeeded = local;
                        }

                        //Check defining type matches instance, and check params.
                        if (Utils.AreManagedAndCppTypesEqual(LibCpp2ILUtils.WrapType(m.DeclaringType!), InstanceBeingCalledOn.Type))
                        {
                            possibleTarget = m;

                            if (!MethodUtils.CheckParameters(instruction, m, context, true, out Arguments, InstanceBeingCalledOn))
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
                        if (LibCpp2IlMain.Binary!.is32Bit && context.Stack.TryPeek(out var op) && op is LocalDefinition local)
                        {
                            InstanceBeingCalledOn = local;
                            toPushBackIfNeeded = local;
                        }

                        //Methods which are non-static and for which the declaring type is some form of supertype of the object we're calling on.
                        var baseClassMethods = listOfCallableMethods.Where(m => !m.IsStatic && Utils.IsManagedTypeAnInstanceOfCppOne(LibCpp2ILUtils.WrapType(m.DeclaringType!), InstanceBeingCalledOn.Type)).ToList();
                        
                        if (baseClassMethods.Count == 0 && toPushBackIfNeeded != null)
                        {
                            //mismatch, push back instance
                            context.Stack.Push(toPushBackIfNeeded);
                        }

                        if (baseClassMethods.Count == 1)
                        {
                            possibleTarget = baseClassMethods.Single();
                            IsCallToSuperclassMethod = true;
                            
                            //Only one method - we can be less strict on leftover parameters.
                            if(!MethodUtils.CheckParameters(instruction, baseClassMethods.Single(), context, true, out Arguments, InstanceBeingCalledOn, false))
                                AddComment("Parameter mismatch, but there is only one method here for which the instance matches.");
                        }
                        else
                        {
                            foreach (var m in baseClassMethods)
                            {
                                //Check params, and be strict about it (no leftover arguments).
                                if (MethodUtils.CheckParameters(instruction, m, context, true, out Arguments, InstanceBeingCalledOn))
                                {
                                    possibleTarget = m;
                                    IsCallToSuperclassMethod = true;
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
                                    if (MethodUtils.CheckParameters(instruction, m, context, true, out Arguments, InstanceBeingCalledOn, false))
                                    {
                                        possibleTarget = m;
                                        IsCallToSuperclassMethod = true;
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
                    possibleTarget = listOfCallableMethods.FirstOrDefault(m => m.IsStatic && MethodUtils.CheckParameters(instruction, m, context, false, out Arguments, InstanceBeingCalledOn));
                else if (LibCpp2IlMain.Binary!.is32Bit && context.Stack.TryPeek(out var op) && op is LocalDefinition local)
                    //Instance method
                    InstanceBeingCalledOn = local; //32-bit and we have an instance
            }

            if (possibleTarget == null && LibCpp2IlMain.Binary.ConcreteGenericImplementationsByAddress.TryGetValue(_jumpTarget, out var concreteGenericMethods))
            {
                AddComment($"Probably a jump to a concrete generic method, there are {concreteGenericMethods.Count} here.");
            }

            //Resolve unmanaged => managed method.
            if (possibleTarget != null)
                ManagedMethodBeingCalled = SharedState.UnmanagedToManagedMethods[possibleTarget];
            else
                AddComment($"Failed to resolve any matching method (there are {listOfCallableMethods.Count} at this address)");

            CreateLocalForReturnType(context);
            
            RegisterLocals();

            if (ManagedMethodBeingCalled?.FullName == "System.Void System.Runtime.CompilerServices.RuntimeHelpers::InitializeArray(System.Array,System.RuntimeFieldHandle)")
            {
                if (Arguments?.Count > 1 && Arguments[1] is ConstantDefinition {Value: FieldDefinition fieldDefinition} && Arguments[0] is LocalDefinition {KnownInitialValue: AllocatedArray arr})
                {
                    instantiatedArrayValues = Utils.ReadArrayInitializerForFieldDefinition(fieldDefinition, arr);
                    wasArrayInstantiation = true;
                    AddComment("Initializes array containing values: " + instantiatedArrayValues.ToStringEnumerable());
                }
            }
        }

        public override string? ToPsuedoCode()
        {
            if (wasArrayInstantiation)
            {
                var arrayType = ((ArrayType) ((LocalDefinition) Arguments[0]).Type).ElementType;
                return $"{Arguments![0].GetPseudocodeRepresentation()} = new {arrayType}[] {{{string.Join(", ", instantiatedArrayValues!)}}}";
            }

            return base.ToPsuedoCode();
        }
    }
}