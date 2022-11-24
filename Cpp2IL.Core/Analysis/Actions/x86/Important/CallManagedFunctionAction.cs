using System;
using System.Linq;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class CallManagedFunctionAction : BaseX86CallAction
    {
        private readonly ulong _jumpTarget;

        private bool wasArrayInstantiation;
        private long[]? instantiatedArrayValues = null;

        public CallManagedFunctionAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            _jumpTarget = instruction.NearBranchTarget;

            if (!LibCpp2IlMain.Binary!.is32Bit)
                InstanceBeingCalledOn = context.GetLocalInReg("rcx"); //64-bit, we can get this immediately, no penalty if this is a static method.


            if (LibCpp2IlMain.Binary!.ConcreteGenericImplementationsByAddress.TryGetValue(_jumpTarget, out var concMethods))
            {
                var genericMethodConstants = context.Constants.Where(c => c.Value is MethodReference or GenericMethodReference).ToList();
                
                ConstantDefinition? matchingConstant = null;
                foreach (var m in concMethods)
                {
                    var managedBaseMethod = m.BaseMethod.AsManaged();
                    
                    matchingConstant = genericMethodConstants.LastOrDefault(conMtd =>
                    {
                        if (conMtd.Value is MethodReference methodReference)
                            return methodReference.Resolve() == managedBaseMethod.Resolve();
                        return ((GenericMethodReference) conMtd.Value).Method.Resolve() == managedBaseMethod.Resolve();
                    });

                    if (matchingConstant != null)
                        break;
                }

                if (matchingConstant != null)
                {
                    MethodReference locatedMethod;
                    if (matchingConstant.Value is MethodReference value)
                        locatedMethod = value;
                    else 
                        locatedMethod = ((GenericMethodReference) matchingConstant.Value).Method;

                    AddComment("Method resolved from concrete implementations at this address, with the help of a constant value to identify which concrete implementation.");

                    if (locatedMethod.HasThis && LibCpp2IlMain.Binary!.is32Bit && context.Stack.TryPeek(out var op) && op is LocalDefinition local)
                    {
                        InstanceBeingCalledOn = local; //32-bit and we have an instance
                        context.Stack.Pop(); //remove instance from stack
                    }

                    if (!MethodUtils.CheckParameters(instruction, locatedMethod, context, locatedMethod.HasThis, out Arguments, InstanceBeingCalledOn?.Type, failOnLeftoverArgs: false))
                        AddComment("parameters do not match, but concrete method was resolved from a constant in a register.");

                    if (locatedMethod.HasThis && InstanceBeingCalledOn?.Type != null && !locatedMethod.DeclaringType.Resolve().IsAssignableFrom(InstanceBeingCalledOn.Type))
                        AddComment($"This is an instance method, but the type of the 'this' parameter is mismatched. Expecting {locatedMethod.Resolve()?.DeclaringType.Name}, actually {InstanceBeingCalledOn.Type.FullName}");
                    else if (locatedMethod.HasThis && InstanceBeingCalledOn?.Type != null)
                    {
                        //Matching type, but is it us or a base type?
                        IsCallToSuperclassMethod = locatedMethod.DeclaringType.Resolve() != InstanceBeingCalledOn.Type?.Resolve();
                    }

                    ManagedMethodBeingCalled = locatedMethod;
                    HandleReturnType(context);
                    return;
                }
                
                //No concrete implementation, fall through to normal
            }

            var listOfCallableMethods = LibCpp2IlMain.GetManagedMethodImplementationsAtAddress(_jumpTarget);

            if (listOfCallableMethods == null)
            {
                AddComment("Could not find any callable methods. Bailing out.");
                return;
            }

            Il2CppMethodDefinition? possibleTarget = null;
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

                if (!possibleTarget.IsStatic && InstanceBeingCalledOn?.Type != null && !possibleTarget.DeclaringType!.AsManaged().IsAssignableFrom(InstanceBeingCalledOn.Type))
                    AddComment($"This is an instance method, but the type of the 'this' parameter is mismatched. Expecting {possibleTarget.DeclaringType.Name}, actually {InstanceBeingCalledOn.Type.FullName}");
                else if (!possibleTarget.IsStatic && InstanceBeingCalledOn?.Type != null)
                {
                    //Matching type, but is it us or a base type?
                    IsCallToSuperclassMethod = possibleTarget.DeclaringType.AsManaged() != InstanceBeingCalledOn.Type?.Resolve();
                }
            }
            else
            {
                //Find the correct method.
                //Have to pop out the instance arg here so we can check the rest of the params, but save it in case we need to push it back.
                LocalDefinition toPushBackIfNeeded = null;
                if (LibCpp2IlMain.Binary!.is32Bit && context.Stack.TryPeek(out var op) && op is LocalDefinition local)
                {
                    InstanceBeingCalledOn = local;
                    context.Stack.Pop(); //Pop this for now
                    toPushBackIfNeeded = local;
                }

                if (InstanceBeingCalledOn?.Type != null)
                {
                    //Direct instance methods take priority
                    possibleTarget = null;
                    foreach (var m in listOfCallableMethods)
                    {
                        if (m.IsStatic) continue; //Only checking instance methods here.
                        
                        //Check defining type matches instance, and check params.
                        if (m.DeclaringType!.AsManaged() == InstanceBeingCalledOn.Type.Resolve())
                        {
                            possibleTarget = m;

                            if (!MethodUtils.CheckParameters(instruction, m, context, true, out Arguments, InstanceBeingCalledOn, false))
                            {
                                var thisIdx = listOfCallableMethods.IndexOf(m);
                                if(listOfCallableMethods.Skip(thisIdx - 1).Any(otherMethod => otherMethod != m && otherMethod.declaringTypeIdx == m.declaringTypeIdx))
                                    //Other matching instance methods are present, check those.
                                    continue;
                                AddComment("parameters do not match, but declaring type of method matches instance.");
                            }

                            break;
                        }
                    }

                    //Check for base class instance methods
                    if (possibleTarget == null)
                    {
                        //Methods which are non-static and for which the declaring type is some form of supertype of the object we're calling on.
                        var baseClassMethods = listOfCallableMethods.Where(m => !m.IsStatic && m.DeclaringType!.AsManaged().IsAssignableFrom(InstanceBeingCalledOn.Type)).ToList();

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
                            if (!MethodUtils.CheckParameters(instruction, baseClassMethods.Single(), context, true, out Arguments, InstanceBeingCalledOn, false))
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
                    
                    if (toPushBackIfNeeded != null && possibleTarget == null)
                    {
                        //Mismatch - re-push the instance
                        context.Stack.Push(toPushBackIfNeeded);
                    }
                }

                if (possibleTarget == null)
                    //Check static methods. No need for a complicated foreach here, we don't have to worry about an instance.
                    possibleTarget = listOfCallableMethods.FirstOrDefault(m => m.IsStatic && MethodUtils.CheckParameters(instruction, m, context, false, out Arguments, InstanceBeingCalledOn));
            }

            //Resolve unmanaged => managed method.
            if (possibleTarget != null)
                ManagedMethodBeingCalled = SharedState.UnmanagedToManagedMethods[possibleTarget];
            else
            {
                var methodSigs = listOfCallableMethods.Select(m => m.DeclaringType?.FullName + "::" + m.Name).Take(10).ToList();
                AddComment($"Failed to resolve any matching method (there are {listOfCallableMethods.Count} at this address - {string.Join(", ", methodSigs)}).");
            }

            if (ManagedMethodBeingCalled != null && MethodUtils.GetMethodInfoArg(ManagedMethodBeingCalled, context) is ConstantDefinition {Value: GenericMethodReference gmr} arg)
            {
                ManagedMethodBeingCalled = gmr.Method;

                if (gmr.Type is GenericInstanceType git)
                    ManagedMethodBeingCalled = gmr.Method.MakeMethodOnGenericType(git.GenericArguments.ToArray());
            }

            HandleReturnType(context);
        }

        public static string[] ourData;
        public static string GetSomething(string id)
        {
            return ourData.First(i => string.Equals(id, i));
        }

        private void HandleReturnType(MethodAnalysis<Instruction> context)
        {
            CreateLocalForReturnType(context);

            RegisterLocals(context);

            if (ManagedMethodBeingCalled?.FullName == "System.Void System.Runtime.CompilerServices.RuntimeHelpers::InitializeArray(System.Array,System.RuntimeFieldHandle)")
            {
                if (Arguments?.Count > 1 && Arguments[1] is ConstantDefinition {Value: FieldDefinition fieldDefinition} && Arguments[0] is LocalDefinition {KnownInitialValue: AllocatedArray arr})
                {
                    instantiatedArrayValues = AnalysisUtils.ReadArrayInitializerForFieldDefinition(fieldDefinition, arr);
                    wasArrayInstantiation = true;
                    AddComment("Initializes array containing values: " + instantiatedArrayValues.ToStringEnumerable());
                }
            }
        }

        public override string? ToPsuedoCode()
        {
            if (wasArrayInstantiation)
            {
                var arrayType = ((ArrayType) ((LocalDefinition) Arguments![0]!).Type!).ElementType;
                return $"{Arguments![0]!.GetPseudocodeRepresentation()} = new {arrayType}[] {{{string.Join(", ", instantiatedArrayValues!)}}}";
            }

            return base.ToPsuedoCode();
        }
    }
}