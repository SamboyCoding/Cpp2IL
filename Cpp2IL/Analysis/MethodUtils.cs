using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection.Metadata;
using Cpp2IL.Analysis.Actions.Important;
using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using GenericParameter = Mono.Cecil.GenericParameter;
using MemberReference = Mono.Cecil.MemberReference;
using MethodDefinition = Mono.Cecil.MethodDefinition;
using TypeReference = Mono.Cecil.TypeReference;

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

            return LibCpp2IlMain.Binary!.is32Bit ? CheckParameters32(associatedInstruction, method, context, isInstance, beingCalledOn, out arguments) : CheckParameters64(method, context, isInstance, out arguments, beingCalledOn, failOnLeftoverArgs);
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
                var arg = actualArgs.RemoveAndReturn(0);

                if (parameterType is GenericParameter gp)
                {
                    var temp = GenericInstanceUtils.ResolveGenericParameterType(gp, beingCalledOn, method);

                    if (temp == null)
                    {
                        //Infer from context - we *assume* that whatever the argument is, is the type of this generic param.
                        //As we have already checked for any parameter containing the generic method.
                        if (arg is LocalDefinition l)
                            parameterType = l.Type;
                    }
                    
                    temp ??= parameterType;
                    parameterType = temp;
                }
                
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
                        if(local.Type is GenericParameter && parameterType is GenericParameter && local.Type.Name == parameterType.Name)
                            break;
                        if(local.KnownInitialValue is int i && i == 0)
                            break; //Null.
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
            
            if (actualArgs.Count == 1 && actualArgs[0] is ConstantDefinition {Value: MethodReference _} c)
            {
                var reg = context.GetConstantInReg("rcx") == c ? "rcx" : context.GetConstantInReg("rdx") == c ? "rdx" : context.GetConstantInReg("r8") == c ? "r8" : "r9";
                context.ZeroRegister(reg);
            }

            arguments = tempArgs;
            return true;
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

        public static MethodDefinition? GetMethodFromVtableSlot(Il2CppTypeDefinition klass, int slotNum)
        {
            try
            {
                var usage = klass.VTable[slotNum];

                if (usage != null)
                    return SharedState.UnmanagedToManagedMethods[usage.AsMethod()];
            }
            catch (IndexOutOfRangeException)
            {
                //Ignore
            }

            if (!SharedState.ConcreteImplementations.ContainsKey(klass))
                return null;

            //Find concrete implementation - this method is abstract
            var concrete = SharedState.ConcreteImplementations[klass];

            try
            {
                var concreteUsage = concrete.VTable[slotNum];
                var concreteMethod = concreteUsage!.AsMethod();

                var unmanagedMethod = klass.Methods!.First(m => m.Name == concreteMethod.Name && m.parameterCount == concreteMethod.parameterCount);

                return SharedState.UnmanagedToManagedMethods[unmanagedMethod];
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }

        }
    }
}