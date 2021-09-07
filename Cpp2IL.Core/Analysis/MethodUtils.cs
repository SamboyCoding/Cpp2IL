using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.Actions.Important;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using GenericParameter = Mono.Cecil.GenericParameter;
using MemberReference = Mono.Cecil.MemberReference;
using MethodDefinition = Mono.Cecil.MethodDefinition;
using TypeReference = Mono.Cecil.TypeReference;

namespace Cpp2IL.Core.Analysis
{
    public class MethodUtils
    {
        private static readonly string[] NON_FP_REGISTERS_BY_IDX = {"rcx", "rdx", "r8", "r9"};
        
        public static bool CheckParameters<T>(T associatedInstruction, Il2CppMethodDefinition method, MethodAnalysis<T> context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments, LocalDefinition? objectMethodBeingCalledOn, bool failOnLeftoverArgs = true)
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

        public static bool CheckParameters<T>(T associatedInstruction, MethodReference method, MethodAnalysis<T> context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments, TypeReference? beingCalledOn = null, bool failOnLeftoverArgs = true)
        {
            if (beingCalledOn == null)
                beingCalledOn = method.DeclaringType;

            return LibCpp2IlMain.Binary!.is32Bit ? CheckParameters32(associatedInstruction, method, context, isInstance, beingCalledOn, out arguments) : CheckParameters64(method, context, isInstance, out arguments, beingCalledOn, failOnLeftoverArgs);
        }

        private static IAnalysedOperand? GetValueFromAppropriateReg<T>(bool? isFloatingPoint, string fpReg, string normalReg, MethodAnalysis<T> context)
        {
            if(isFloatingPoint == true)
                if (context.GetOperandInRegister(fpReg) is { } fpVal)
                    return fpVal;

            return context.GetOperandInRegister(normalReg);
        }

        private static bool CheckParameters64<T>(MethodReference method, MethodAnalysis<T> context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments, TypeReference beingCalledOn, bool failOnLeftoverArgs = true)
        {
            arguments = null;

            var actualArgs = new List<IAnalysedOperand?>();
            if (!isInstance)
                actualArgs.Add(context.GetOperandInRegister("rcx") ?? context.GetOperandInRegister("xmm0"));

            actualArgs.Add(GetValueFromAppropriateReg(method.Parameters.GetValueSafely(0)?.ParameterType?.ShouldBeInFloatingPointRegister(), "xmm1", "rdx", context));
            actualArgs.Add(GetValueFromAppropriateReg(method.Parameters.GetValueSafely(1)?.ParameterType?.ShouldBeInFloatingPointRegister(), "xmm2", "r8", context));
            actualArgs.Add(GetValueFromAppropriateReg(method.Parameters.GetValueSafely(2)?.ParameterType?.ShouldBeInFloatingPointRegister(), "xmm3", "r9", context));

            if (actualArgs.FindLast(a => a is ConstantDefinition {Value: MethodReference _}) is ConstantDefinition {Value: MethodReference actualGenericMethod})
            {
                if (actualGenericMethod.Name == method.Name && actualGenericMethod.DeclaringType == method.DeclaringType)
                    method = actualGenericMethod;
                else
                    return false; //We have a method which isn't this one.
            }

            var tempArgs = new List<IAnalysedOperand>();
            var stackOffset = 0x20;
            foreach (var parameterData in method.Parameters!)
            {
                var parameterType = parameterData.ParameterType;

                if (parameterType == null)
                    throw new ArgumentException($"Parameter \"{parameterData}\" of method {method.FullName} has a null type??");

                IAnalysedOperand arg;
                if (actualArgs.Count == 0)
                {
                    //Read from stack
                    if (!context.StackStoredLocals.ContainsKey(stackOffset))
                        //No stack arg, fail
                        return false;

                    arg = context.StackStoredLocals[stackOffset];
                    stackOffset += 8; //TODO Adjust for size of operand?
                }
                else if (actualArgs.All(a => a == null))
                    //We have only null args, so we're not done through the registers, but out of actual args. Fail.
                    return false;
                else
                    arg = actualArgs.RemoveAndReturn(0)!;

                if (parameterType is GenericParameter gp)
                {
                    var temp = GenericInstanceUtils.ResolveGenericParameterType(gp, beingCalledOn, method);

                    if (temp == null)
                    {
                        //Infer from context - we *assume* that whatever the argument is, is the type of this generic param.
                        //As we have already checked for any parameter containing the generic method.
                        if (arg is LocalDefinition {Type: { }} l)
                            parameterType = l.Type;
                    }

                    temp ??= parameterType;
                    parameterType = temp;
                }

                if (arg is ConstantDefinition {Value: StackPointer p})
                {
                    if(context.StackStoredLocals.TryGetValue((int) p.offset, out var loc))
                        arg = loc;
                }

                switch (arg)
                {
                    //We assert parameter type to be non-null in all of these cases, because we've null-checked the default value further up.

                    case ConstantDefinition cons when cons.Type.FullName != parameterType!.ToString(): //Constant type mismatch
                        if (parameterType.Resolve()?.IsEnum == true && cons.Type.IsPrimitive)
                            break; //Forgive primitive => enum coercion.
                        if (parameterType.IsPrimitive && cons.Type.IsPrimitive)
                            break; //Forgive primitive coercion.
                        if ((parameterType.FullName == "System.String" || parameterType.FullName == "System.Object") && cons.Value is string)
                            break;
                        if (parameterType.IsPrimitive && cons.Value is Il2CppString cppString)
                        {
                            var primitiveLength = Utils.GetSizeOfObject(parameterType);
                            ulong newValue;
                            if (primitiveLength == 8)
                                newValue = LibCpp2IlMain.Binary!.ReadClassAtVirtualAddress<ulong>(cppString.Address);
                            else if (primitiveLength == 4)
                                newValue = LibCpp2IlMain.Binary!.ReadClassAtVirtualAddress<uint>(cppString.Address);
                            else if (primitiveLength == 1)
                                newValue = LibCpp2IlMain.Binary!.ReadClassAtVirtualAddress<byte>(cppString.Address);
                            else
                                throw new Exception($"'string' -> primitive: Not implemented: Size {primitiveLength}, type {parameterType}");

                            if (parameterType.Name == "Single")
                                cons.Value = BitConverter.ToSingle(BitConverter.GetBytes((uint) newValue), 0);
                            else if (parameterType.Name == "Double")
                                cons.Value = BitConverter.ToDouble(BitConverter.GetBytes(newValue), 0);
                            else
                                cons.Value = newValue;

                            cons.Type = Type.GetType(parameterType.FullName!)!;
                            break;
                        }

                        if (parameterType.IsPrimitive && cons.Value is UnknownGlobalAddr unknownGlobalAddr)
                        {
                            Utils.CoerceUnknownGlobalValue(parameterType, unknownGlobalAddr, cons);
                            break;
                        }
                        if (typeof(MemberReference).IsAssignableFrom(cons.Type) && parameterType.Name == "IntPtr")
                            break; //We allow this, because an IntPtr is usually a type or, more commonly, method pointer.
                        if (typeof(FieldReference).IsAssignableFrom(cons.Type) && parameterType.Name == "RuntimeFieldHandle")
                            break; //These are the same struct - we represent it as a FieldReference but it's actually a runtime field handle.
                        if (typeof(TypeReference).IsAssignableFrom(cons.Type) && parameterType.Name == "RuntimeTypeHandle")
                            break; //These are the same struct - we represent it as a TypeReference but it's actually a runtime type handle.
                        return false;
                    case LocalDefinition local:
                        if (parameterType.IsArray && local.Type?.IsArray != true)
                            return false; //Fail. Array<->non array is non-forgivable.
                        if(local.Type != null && parameterType!.Resolve().IsAssignableFrom(local.Type))
                            //"Success" condition, all matches
                            break;
                        if (parameterType!.IsPrimitive && local.Type?.IsPrimitive == true)
                            break; //Forgive primitive coercion.
                        if (local.Type?.IsArray == true && parameterType.Resolve().IsAssignableFrom(Utils.ArrayReference))
                            break;
                        if (local.Type is GenericParameter && parameterType is GenericParameter && local.Type.Name == parameterType.Name)
                            break;
                        if (local.KnownInitialValue is int i && i == 0)
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

        private static bool CheckParameters32<T>(T associatedInstruction, MethodReference method, MethodAnalysis<T> context, bool isInstance, TypeReference beingCalledOn, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments)
        {
            arguments = new();

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
                            var allocateInstanceAction = AbstractNewObjAction<T>.Make<T>(context, associatedInstruction, structTypeDef);
                            context.Actions.Add(allocateInstanceAction);

                            var instanceLocal = allocateInstanceAction.LocalReturned;

                            //Set the fields from the operands
                            for (var i = 0; i < listOfStackArgs.Count; i++)
                            {
                                var associatedField = fieldsToCheck[i];

                                var stackArg = listOfStackArgs[i];
                                if (stackArg is LocalDefinition local)
                                    //I'm sorry for what the next line contains.
                                    context.Actions.Add((BaseAction<T>)(object) new RegToFieldAction((MethodAnalysis<Instruction>)(object) context, (Instruction) (object) associatedInstruction, FieldUtils.FieldBeingAccessedData.FromDirectField(associatedField), (LocalDefinition)(object) instanceLocal!, (LocalDefinition)(object) local));
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

        private static void RePushStack<T>(List<IAnalysedOperand> toRepush, MethodAnalysis<T> context)
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

                Il2CppMethodDefinition? unmanagedMethod = null;
                var thisType = klass;

                while (thisType != null && unmanagedMethod == null)
                {
                    unmanagedMethod = thisType.Methods!.FirstOrDefault(m => m.Name == concreteMethod.Name && m.parameterCount == concreteMethod.parameterCount);
                    thisType = thisType.BaseType?.baseType;
                }

                if (unmanagedMethod == null)
                    //Let's just give a little more context
                    throw new Exception($"GetMethodFromVtableSlot: Looking for base method of {concreteMethod.HumanReadableSignature} (concretely defined in {concreteMethod.DeclaringType!.FullName}), in type {klass.FullName}, for vtable slot {slotNum}, but couldn't find it?");

                return SharedState.UnmanagedToManagedMethods[unmanagedMethod];
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
        }

        public static IAnalysedOperand? GetMethodInfoArg<T>(MethodReference managedMethodBeingCalled, MethodAnalysis<T> context)
        {
            if (LibCpp2IlMain.Binary!.is32Bit)
            {
                //Already should have popped off all the arguments, just peek-and-pop one more
                if (context.Stack.Peek() is ConstantDefinition {Value: GenericMethodReference _})
                    return context.Stack.Pop();

                return null;
            }
            
            var paramIdx = managedMethodBeingCalled.Parameters.Count;

            if (managedMethodBeingCalled.HasThis)
                paramIdx++;

            if (paramIdx < 4)
                return context.GetOperandInRegister(NON_FP_REGISTERS_BY_IDX[paramIdx]);

            var stackOffset = 0x20 + Utils.GetPointerSizeBytes() * (paramIdx - 4);

            context.StackStoredLocals.TryGetValue(stackOffset, out var ret);

            return ret;
        }
    }
}