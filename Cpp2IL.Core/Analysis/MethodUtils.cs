using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.Actions.x86.Important;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Exceptions;
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
    public static class MethodUtils
    {
        private static readonly string[] NON_FP_REGISTERS_BY_IDX = {"rcx", "rdx", "r8", "r9"};
        
        public static bool CheckParameters<T>(T associatedInstruction, Il2CppMethodDefinition method, MethodAnalysis<T> context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments, LocalDefinition? objectMethodBeingCalledOn, bool failOnLeftoverArgs = true)
        {
            MethodReference managedMethod = SharedState.UnmanagedToManagedMethods[method];

            TypeReference? beingCalledOn = null;
            var objectType = objectMethodBeingCalledOn?.Type;
            if ((managedMethod.HasGenericParameters || managedMethod.DeclaringType.HasGenericParameters) && objectType is GenericInstanceType {HasGenericArguments: true} git)
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

            return LibCpp2IlMain.Binary!.InstructionSet switch
            {
                InstructionSet.X86_32 => CheckParameters32(associatedInstruction, method, context, isInstance, beingCalledOn, out arguments),
                InstructionSet.X86_64 => CheckParameters64(method, context, isInstance, out arguments, beingCalledOn, failOnLeftoverArgs),
                InstructionSet.ARM64 => CheckParametersArmV8(method, context, isInstance, out arguments),
                _ => throw new UnsupportedInstructionSetException(),
            };
        }

        private static IAnalysedOperand? GetValueFromAppropriateX64Reg<T>(bool? isFloatingPoint, string fpReg, string normalReg, MethodAnalysis<T> context)
        {
            if(isFloatingPoint == true)
                if (context.GetOperandInRegister(fpReg) is { } fpVal)
                    return fpVal;

            return context.GetOperandInRegister(normalReg);
        }

        private static bool CheckSingleParamNew(IAnalysedOperand arg, TypeReference parameterType)
        {
            switch (arg)
            {
                case ConstantDefinition cons when cons.Type.FullName != parameterType.ToString(): //Constant type mismatch
                    if (parameterType.Resolve()?.IsEnum == true && cons.Type.IsPrimitive)
                        break; //Forgive primitive => enum coercion.
                    if (parameterType.IsPrimitive && cons.Type.IsPrimitive)
                    {
                        if (parameterType.Name is not "IntPtr" && cons.Value is IConvertible ic)
                        {
                            //Correct primitive type - fixes boolean arguments etc.
                            cons.Type = typeof(int).Module.GetType(parameterType.FullName);
                            cons.Value = Utils.ReinterpretBytes(ic, parameterType);
                        }

                        break; //Forgive primitive coercion.
                    }

                    if (parameterType.FullName is "System.String" or "System.Object" && cons.Value is string)
                        break; //Forgive unmanaged string literal as managed string or object param
                    if (parameterType.IsPrimitive && cons.Value is Il2CppString {HasBeenUsedAsAString: false} cppString)
                    {
                        //Il2CppString contains any unknown global address that looks vaguely like a string
                        //We try and re-interpret it here, most commonly as a floating point value, as integer constants are usually immediate values.
                        var primitiveLength = Utils.GetSizeOfObject(parameterType);
                        var newValue = primitiveLength switch
                        {
                            8 => LibCpp2IlMain.Binary!.ReadClassAtVirtualAddress<ulong>(cppString.Address),
                            4 => LibCpp2IlMain.Binary!.ReadClassAtVirtualAddress<uint>(cppString.Address),
                            1 => LibCpp2IlMain.Binary!.ReadClassAtVirtualAddress<byte>(cppString.Address),
                            _ => throw new Exception($"'string' -> primitive: Not implemented: Size {primitiveLength}, type {parameterType}")
                        };

                        //Reinterpret floating-point bytes
                        cons.Value = parameterType.Name switch
                        {
                            "Single" => BitConverter.ToSingle(BitConverter.GetBytes((uint)newValue), 0),
                            "Double" => BitConverter.ToDouble(BitConverter.GetBytes(newValue), 0),
                            _ => newValue
                        };

                        //Correct type
                        cons.Type = Type.GetType(parameterType.FullName!)!;
                        break;
                    }

                    if (parameterType.IsPrimitive && cons.Value is UnknownGlobalAddr unknownGlobalAddr)
                    {
                        //Try get unknown global values as a constant
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
                        return false; //Absolutely do not forgive parameters which expect an array, and we're passing a single value
                    if (local.Type != null && parameterType.Resolve().IsAssignableFrom(local.Type))
                        //Basic "Success" condition, parameter type matches type of local
                        break;
                    if (parameterType.IsPrimitive && local.Type?.IsPrimitive == true)
                        break; //Forgive primitive coercion.
                    if (local.Type?.IsArray == true && parameterType.Resolve().IsAssignableFrom(Utils.ArrayReference))
                        break; //Forgive IEnumerables etc
                    if (local.Type is GenericParameter && parameterType is GenericParameter && local.Type.Name == parameterType.Name)
                        break; //Unknown generic params which share a name. Not sure this is needed.
                    if (local.KnownInitialValue is 0)
                        break; //Literal null value. This is ok.
                    return false;
            }

            return true;
        }

        private static bool CheckParameters64<T>(MethodReference method, MethodAnalysis<T> context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments, TypeReference beingCalledOn, bool failOnLeftoverArgs = true)
        {
            arguments = null;

            var actualArgs = new List<IAnalysedOperand?>();
            if (!isInstance)
                actualArgs.Add(context.GetOperandInRegister("rcx") ?? context.GetOperandInRegister("xmm0"));

            actualArgs.Add(GetValueFromAppropriateX64Reg(method.Parameters.GetValueSafely(0)?.ParameterType?.ShouldBeInFloatingPointRegister(), "xmm1", "rdx", context));
            actualArgs.Add(GetValueFromAppropriateX64Reg(method.Parameters.GetValueSafely(1)?.ParameterType?.ShouldBeInFloatingPointRegister(), "xmm2", "r8", context));
            actualArgs.Add(GetValueFromAppropriateX64Reg(method.Parameters.GetValueSafely(2)?.ParameterType?.ShouldBeInFloatingPointRegister(), "xmm3", "r9", context));

            if (actualArgs.FindLast(a => a is ConstantDefinition {Value: MethodReference _}) is ConstantDefinition {Value: MethodReference actualGenericMethod})
            {
                if (actualGenericMethod.Name == method.Name && actualGenericMethod.DeclaringType == method.DeclaringType)
                    method = actualGenericMethod;
            }

            if (actualArgs.FindLast(a => a is ConstantDefinition { Value: GenericMethodReference _ }) is ConstantDefinition { Value: GenericMethodReference actualGenericMethodRef })
            {
                var agm = actualGenericMethodRef.Method;
                if (agm.Name == method.Name && agm.DeclaringType == method.DeclaringType)
                    method = agm;
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

                if (!CheckSingleParamNew(arg, parameterType)) 
                    return false;

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
                if (CheckSingleParamNew(value, parameterData.ParameterType))
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
                    if (structTypeDef != null && context.Stack.Count >= fieldsToCheck!.Count)
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
                            allStructFieldsMatch &= CheckSingleParamNew(actualArg, structField.FieldType);
                        }

                        if (allStructFieldsMatch)
                        {
                            //Now we just have to push the actions required to simulate a full creation of this struct.
                            //So an allocation of the struct, setting of fields, and then push the struct local to listToRePush
                            //as its used as the arguments

                            //Allocate an instance of the struct
                            var allocateInstanceAction = AbstractNewObjAction<T>.Make(context, associatedInstruction, structTypeDef);
                            context.Actions.Add(allocateInstanceAction);

                            var instanceLocal = allocateInstanceAction.LocalReturned!;

                            //Set the fields from the operands
                            for (var i = 0; i < listOfStackArgs.Count; i++)
                            {
                                var associatedField = fieldsToCheck[i];

                                var stackArg = listOfStackArgs[i];
                                if (stackArg is LocalDefinition local)
                                    //I'm sorry for what the next line contains.
                                    context.Actions.Add((BaseAction<T>)(object) new RegToFieldAction((MethodAnalysis<Instruction>)(object) context, (Instruction) (object) associatedInstruction!, FieldUtils.FieldBeingAccessedData.FromDirectField(associatedField), instanceLocal, local));
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

        private static bool CheckParametersArmV8<T>(MethodReference method, MethodAnalysis<T> context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments)
        {
            //See MethodAnalysis#HandleArm64Parameters for a detailed explanation of how this works.
            arguments = null;
            
            var xCount = isInstance ? 1 : 0;
            var vCount = 0;
            
            var ret = new List<IAnalysedOperand>();
            foreach (var parameterDefinition in method.Parameters)
            {
                //Floating point -> v reg, else -> x reg
                var reg = parameterDefinition.ParameterType.ShouldBeInFloatingPointRegister() ? $"v{vCount++}" : $"x{xCount++}";

                if (reg[^1] >= '8')
                    return false; //TODO stack support. Probably not needed often.
                
                var operand = context.GetOperandInRegister(reg);

                if (operand == null)
                    //Missing an arg - instant fail.
                    return false;

                if (!CheckSingleParamNew(operand, parameterDefinition.ParameterType))
                    //Mismatched type.
                    return false; 
                
                ret.Add(operand);
            }

            arguments = ret;
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
            switch (LibCpp2IlMain.Binary!.InstructionSet)
            {
                case InstructionSet.X86_32 when context.Stack.Peek() is ConstantDefinition {Value: GenericMethodReference _}:
                    //Already should have popped off all the arguments, just peek-and-pop one more
                    return context.Stack.Pop();
                case InstructionSet.X86_64:
                {
                    var paramIdx = managedMethodBeingCalled.Parameters.Count;

                    if (managedMethodBeingCalled.HasThis)
                        paramIdx++;

                    if (paramIdx < 4)
                        return context.GetOperandInRegister(NON_FP_REGISTERS_BY_IDX[paramIdx]);

                    var stackOffset = 0x20 + Utils.GetPointerSizeBytes() * (paramIdx - 4);

                    context.StackStoredLocals.TryGetValue(stackOffset, out var ret);

                    return ret;
                }
                case InstructionSet.ARM64:
                    var xCount = managedMethodBeingCalled.Resolve().IsStatic ? 0 : 1;

                    foreach (var parameterDefinition in managedMethodBeingCalled.Parameters)
                    {
                        //Floating point -> v reg, else -> x reg
                        if (!parameterDefinition.ParameterType.ShouldBeInFloatingPointRegister())
                            xCount++;
                    }

                    if (xCount > 7)
                        return null;

                    var reg = $"x{xCount}";

                    return context.GetOperandInRegister(reg);
            }

            return null;
        }
    }
}