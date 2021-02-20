using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Cpp2IL.Analysis.Actions.Important;
using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace Cpp2IL.Analysis
{
    public class MethodUtils
    {
        public static bool CheckParameters(Instruction associatedInstruction, Il2CppMethodDefinition method, MethodAnalysis context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments, LocalDefinition? objectMethodBeingCalledOn, bool failOnLeftoverArgs = true)
        {
            MethodReference managedMethod = SharedState.UnmanagedToManagedMethods[method];

            TypeReference? beingCalledOn = null;
            var objectType = objectMethodBeingCalledOn?.Type;
            if (managedMethod.HasGenericParameters && managedMethod.DeclaringType.HasGenericParameters && objectType is GenericInstanceType {HasGenericArguments: true} git)
            {
                managedMethod = Utils.MakeGenericMethodFromType(managedMethod, git);
                beingCalledOn = git;
            }

            return CheckParameters(associatedInstruction, managedMethod, context, isInstance, out arguments, beingCalledOn, failOnLeftoverArgs);
        }

        public static bool CheckParameters(Instruction associatedInstruction, MethodReference method, MethodAnalysis context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments, TypeReference? beingCalledOn = null, bool failOnLeftoverArgs = true)
        {
            if (beingCalledOn == null)
                beingCalledOn = method.DeclaringType;

            return LibCpp2IlMain.ThePe!.is32Bit ? CheckParameters32(associatedInstruction, method, context, isInstance, beingCalledOn, out arguments) : CheckParameters64(method, context, isInstance, out arguments, beingCalledOn, failOnLeftoverArgs);
        }

        private static bool CheckParameters64(MethodReference method, MethodAnalysis context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments, TypeReference beingCalledOn, bool failOnLeftoverArgs = true)
        {
            arguments = null;

            var actualArgs = new List<IAnalysedOperand?>();
            if (!isInstance)
                actualArgs.Add(context.GetOperandInRegister("rcx") ?? context.GetOperandInRegister("xmm0"));

            actualArgs.Add(context.GetOperandInRegister("rdx") ?? context.GetOperandInRegister("xmm1"));
            actualArgs.Add(context.GetOperandInRegister("r8") ?? context.GetOperandInRegister("xmm2"));
            actualArgs.Add(context.GetOperandInRegister("r9") ?? context.GetOperandInRegister("xmm3"));

            if (actualArgs.FindLast(a => a is ConstantDefinition {Value: MethodReference _}) is ConstantDefinition {Value: MethodReference actualGenericMethod})
            {
                if (actualGenericMethod.Name == method.Name && actualGenericMethod.DeclaringType == method.DeclaringType)
                    method = actualGenericMethod;
                else
                    return false; //We have a method which isn't this one.
            }

            var tempArgs = new List<IAnalysedOperand>();
            foreach (var parameterData in method.Parameters!)
            {
                if (actualArgs.Count(a => a != null) == 0) return false;

                var parameterType = parameterData.ParameterType;

                if (parameterType is GenericParameter gp)
                {
                    var temp = ResolveGenericParameterType(method, beingCalledOn, gp);
                    temp ??= parameterType;
                    parameterType = temp;
                }

                var arg = actualArgs.RemoveAndReturn(0);
                switch (arg)
                {
                    case ConstantDefinition cons when cons.Type.FullName != parameterType.ToString(): //Constant type mismatch
                        if (parameterType.IsPrimitive && cons.Type.IsPrimitive)
                            break; //Forgive primitive coercion.
                        if (cons.Type.IsAssignableTo(typeof(MemberReference)) && parameterType.Name == "IntPtr")
                            break; //We allow this, because an IntPtr is usually a type or, more commonly, method pointer.
                        if (cons.Type.IsAssignableTo(typeof(FieldReference)) && parameterType.Name == "RuntimeFieldHandle")
                            break; //These are the same struct - we represent it as a FieldReference but it's actually a runtime field handle.
                        return false;
                    case LocalDefinition local when local.Type == null || !parameterType.Resolve().IsAssignableFrom(local.Type): //Local type mismatch
                        if (parameterType.IsPrimitive && local.Type?.IsPrimitive == true)
                            break; //Forgive primitive coercion.
                        if (local.Type?.IsArray == true && parameterType.Resolve().IsAssignableFrom(Utils.ArrayReference))
                            break;
                        return false;
                }

                //todo handle value types (Structs)

                tempArgs.Add(arg);
            }

            actualArgs = actualArgs.Where(a => a != null && !context.IsEmptyRegArg(a) && !(a is LocalDefinition {KnownInitialValue: 0})).ToList();
            if (failOnLeftoverArgs && actualArgs.Count > 0)
            {
                if (actualArgs.Count != 1 || !(actualArgs[0] is ConstantDefinition {Value: MethodReference reference}) || reference != method)
                {
                    return false; //Left over args - it's probably not this one
                }
            }

            arguments = tempArgs;
            return true;
        }

        private static int GetIndexOfGenericParameterWithName(GenericInstanceType git, string name)
        {
            var res = git.ElementType.GenericParameters.ToList().FindIndex(g => g.Name == name);
            if (res >= 0)
                return res;
            
            return git.GenericArguments.ToList().FindIndex(g => g.Name == name);
        }

        private static TypeReference? GetGenericArgumentByNameFromGenericInstanceType(GenericInstanceType git, GenericParameter gp)
        {
            if (GetIndexOfGenericParameterWithName(git, gp.FullName) is { } genericIdx && genericIdx >= 0)
                return git.GenericArguments[genericIdx];

            return null;
        }

        internal static TypeReference? ResolveGenericParameterType(MethodReference method, TypeReference? instance, GenericParameter gp)
        {
            var git = instance as GenericInstanceType;
            if (git != null && GetGenericArgumentByNameFromGenericInstanceType(git, gp) is { } t)
                return t;

            if (method is GenericInstanceMethod gim && gim.ElementMethod.HasGenericParameters)
            {
                var p = gim.ElementMethod.GenericParameters.ToList();

                if (git != null && git.ElementType.HasGenericParameters)
                    //Filter to generic params not specified in the type
                    p = p.Where(genericParam => git.ElementType.GenericParameters.All(gitP => gitP.FullName != genericParam.FullName)).ToList();

                if (p.FindIndex(g => g.Name == gp.FullName) is { } methodGenericIdx && methodGenericIdx >= 0)
                    return gim.GenericArguments[methodGenericIdx];
            }

            return null;
        }

        private static TypeReference? TryLookupGenericParamBasedOnFunctionArguments(GenericParameter p, MethodReference methodReference, TypeReference?[] argumentTypes)
        {
            if (!methodReference.HasGenericParameters) return null;

            for (var i = 0; i < methodReference.Parameters.Count; i++)
            {
                var parameterType = methodReference.Parameters[i].ParameterType;
                var argumentType = argumentTypes[i];

                if (parameterType.IsGenericInstance && parameterType is GenericInstanceType baseGit && argumentType is GenericInstanceType actualGit && GetIndexOfGenericParameterWithName(baseGit, p.FullName) is { } idx && idx >= 0)
                {
                    return actualGit.GenericArguments[idx];
                }
            }

            return null;
        }

        internal static GenericInstanceType ResolveMethodGIT(GenericInstanceType unresolved, MethodReference method, TypeReference? instance, TypeReference?[] parameterTypes)
        {
            var baseType = unresolved.ElementType;

            var genericArgs = unresolved.GenericArguments.Select(
                ga => !(ga is GenericParameter p) ? ga : ResolveGenericParameterType(method, instance, p) ?? TryLookupGenericParamBasedOnFunctionArguments(p, method, parameterTypes)
            ).ToArray();

            return baseType.MakeGenericInstanceType(genericArgs);
        }

        private static bool CheckParameters32(Instruction associatedInstruction, MethodReference method, MethodAnalysis context, bool isInstance, TypeReference beingCalledOn, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments)
        {
            arguments = new List<IAnalysedOperand>();

            var listToRePush = new List<IAnalysedOperand>();

            //Arguments pushed to stack
            foreach (var parameterData in method.Parameters)
            {
                if (context.Stack.Count == 0)
                {
                    RePushStack(listToRePush, context);
                    return false; //Missing a parameter
                }

                var value = context.Stack.Peek();
                if (CheckSingleParameter(value, parameterData.ParameterType))
                {
                    //This parameter is fine, move on.
                    listToRePush.Add(context.Stack.Pop());
                    arguments.Add(listToRePush.Last());
                    continue;
                }

                if (parameterData.ParameterType.IsValueType && !parameterData.ParameterType.IsPrimitive)
                {
                    //Failed to find a parameter, but the parameter type we're expecting is a struct
                    //Sometimes the arguments are passed individually, because at the end of the day they're just stack offsets.
                    var structTypeDef = parameterData.ParameterType.Resolve();


                    var fieldsToCheck = structTypeDef?.Fields.Where(f => !f.IsStatic).ToList();
                    if (structTypeDef != null && context.Stack.Count >= fieldsToCheck.Count)
                    {
                        //We have enough stack entries to fill the fields.
                        var listOfStackArgs = new List<IAnalysedOperand>();
                        for (var i = 0; i < fieldsToCheck.Count; i++)
                        {
                            listOfStackArgs.Add(context.Stack.Pop());
                        }

                        //Check that all the fields match the expected type.
                        var allStructFieldsMatch = true;
                        for (var i = 0; i < fieldsToCheck.Count; i++)
                        {
                            var structField = fieldsToCheck[i];
                            var actualArg = listOfStackArgs[i];
                            allStructFieldsMatch &= CheckSingleParameter(actualArg, structField.FieldType);
                        }

                        if (allStructFieldsMatch)
                        {
                            //Now we just have to push the actions required to simulate a full creation of this struct.
                            //So an allocation of the struct, setting of fields, and then push the struct local to listToRePush
                            //as its used as the arguments

                            //Allocate an instance of the struct
                            var allocateInstanceAction = new AllocateInstanceAction(context, associatedInstruction, structTypeDef);
                            context.Actions.Add(allocateInstanceAction);

                            var instanceLocal = allocateInstanceAction.LocalReturned;

                            //Set the fields from the operands
                            for (var i = 0; i < listOfStackArgs.Count; i++)
                            {
                                var associatedField = fieldsToCheck[i];

                                var stackArg = listOfStackArgs[i];
                                if (stackArg is LocalDefinition local)
                                    context.Actions.Add(new LocalToFieldAction(context, associatedInstruction, FieldUtils.FieldBeingAccessedData.FromDirectField(associatedField), instanceLocal!, local));
                                else
                                {
                                    //TODO Constants
                                }
                            }

                            //Add the instance to the arguments list.
                            arguments.Add(instanceLocal);

                            //And then move on to the next argument.
                            continue;
                        }

                        //Failure condition

                        //Push
                        listToRePush.AddRange(listOfStackArgs);
                        //Fall-through to the fail below.
                    }
                }


                //Fail condition
                RePushStack(listToRePush, context);
                arguments = null;
                return false;
            }

            return true;
        }

        private static bool CheckSingleParameter(IAnalysedOperand analyzedOperand, TypeReference expectedType)
        {
            switch (analyzedOperand)
            {
                case ConstantDefinition cons when cons.Type.FullName != expectedType.ToString(): //Constant type mismatch
                    //In the case of a constant, check if we can re-interpret.

                    if (expectedType.ToString() == "System.Boolean" && cons.Value is ulong constantNumber)
                    {
                        //Reinterpret as bool.
                        cons.Type = typeof(bool);
                        cons.Value = constantNumber == 1UL;
                        return true;
                    }

                    return false;
                case LocalDefinition local when local.Type == null || !expectedType.Resolve().IsAssignableFrom(local.Type): //Local type mismatch
                    return false;
            }

            return true;
        }

        private static void RePushStack(List<IAnalysedOperand> toRepush, MethodAnalysis context)
        {
            toRepush.Reverse();
            foreach (var analysedOperand in toRepush)
            {
                context.Stack.Push(analysedOperand);
            }
        }
    }
}