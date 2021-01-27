using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Cpp2IL.Analysis.Actions;
using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;
using Mono.Cecil;

namespace Cpp2IL.Analysis
{
    public class MethodUtils
    {
        public static bool CheckParameters(Instruction associatedInstruction, Il2CppMethodDefinition method, MethodAnalysis context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments, bool failOnLeftoverArgs = true)
        {
            var managedMethod = SharedState.UnmanagedToManagedMethods[method];
            return CheckParameters(associatedInstruction, managedMethod, context, isInstance, out arguments, failOnLeftoverArgs);
        }
        
        public static bool CheckParameters(Instruction associatedInstruction, MethodDefinition method, MethodAnalysis context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments, bool failOnLeftoverArgs = true)
        {
            return LibCpp2IlMain.ThePe!.is32Bit ? CheckParameters32(associatedInstruction, method, context, isInstance, out arguments) : CheckParameters64(method, context, isInstance, out arguments, failOnLeftoverArgs);
        }

        private static bool CheckParameters64(MethodDefinition method, MethodAnalysis context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments, bool failOnLeftoverArgs = true)
        {
            arguments = null;

            var actualArgs = new List<IAnalysedOperand>();
            if (!isInstance)
                actualArgs.Add(context.GetOperandInRegister("rcx") ?? context.GetOperandInRegister("xmm0"));

            actualArgs.Add(context.GetOperandInRegister("rdx") ?? context.GetOperandInRegister("xmm1"));
            actualArgs.Add(context.GetOperandInRegister("r8") ?? context.GetOperandInRegister("xmm2"));
            actualArgs.Add(context.GetOperandInRegister("r9") ?? context.GetOperandInRegister("xmm3"));

            var tempArgs = new List<IAnalysedOperand>();
            foreach (var parameterData in method.Parameters!)
            {
                if (actualArgs.Count(a => a != null) == 0) return false;

                var arg = actualArgs.RemoveAndReturn(0);
                switch (arg)
                {
                    case ConstantDefinition cons when cons.Type.FullName != parameterData.ParameterType.ToString(): //Constant type mismatch
                    case LocalDefinition local when local.Type == null || !parameterData.ParameterType.Resolve().IsAssignableFrom(local.Type): //Local type mismatch
                        return false;
                }
                
                //todo handle value types (Structs)

                tempArgs.Add(arg);
            }

            if (failOnLeftoverArgs && actualArgs.Any(a => a != null && !context.IsEmptyRegArg(a)))
                return false; //Left over args - it's probably not this one

            arguments = tempArgs;
            return true;
        }

        private static bool CheckParameters32(Instruction associatedInstruction, MethodDefinition method, MethodAnalysis context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments)
        {
            arguments = null;

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
                    continue;
                }

                if (parameterData.ParameterType.IsValueType && !parameterData.ParameterType.IsPrimitive)
                {
                    //Failed to find a parameter, but the parameter type we're expecting is a struct
                    //Sometimes the arguments are passed individually, because at the end of the day they're just stack offsets.
                    var structTypeDef = parameterData.ParameterType.Resolve();

                    if (associatedInstruction.IP == 0x10B4A625)
                    {

                        Console.WriteLine($"For call to {method} at 0x{associatedInstruction.IP:X} - found a struct arg. Checking we have the required stack entries...");
                        
                        var fieldsToCheck = structTypeDef.Fields.Where(f => !f.IsStatic).ToList();
                        Console.WriteLine($"There are {context.Stack.Count} stack entries and {fieldsToCheck.Count} fields in {structTypeDef.Name}");

                        if (structTypeDef != null && context.Stack.Count >= fieldsToCheck.Count)
                        {
                            Console.WriteLine("...we do. Building list...");
                            //We have enough stack entries to fill the fields.
                            var listOfStackArgs = new List<IAnalysedOperand>();
                            for (var i = 0; i < fieldsToCheck.Count; i++)
                            {
                                listOfStackArgs.Add(context.Stack.Pop());
                            }

                            // listOfStackArgs.Reverse(); //Get in the correct left-to-right order for arguments

                            //Check that all the fields match the expected type.
                            var allStructFieldsMatch = true;
                            Console.WriteLine("Checking field types against parameter types...");
                            for (var i = 0; i < fieldsToCheck.Count; i++)
                            {
                                var structField = fieldsToCheck[i];
                                var actualArg = listOfStackArgs[i];
                                allStructFieldsMatch &= CheckSingleParameter(actualArg, structField.FieldType);
                            }

                            Console.WriteLine($"Matching: {allStructFieldsMatch}");
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

                                listToRePush.Add(instanceLocal);

                                //And then move on to the next argument.
                                continue;
                            }

                            //Failure condition
                            //Re-reverse
                            // listOfStackArgs.Reverse();

                            //Push
                            listToRePush.AddRange(listOfStackArgs);
                            //Fall-through to the fail below.
                        }
                    }
                }
                

                //Fail condition
                RePushStack(listToRePush, context);
                return false;
            }

            arguments = listToRePush.ToArray().ToList(); //Clone list
            // RePushStack(listToRePush, context); //todo: in the event of this being the right method - we don't want to re-push, right? as the method consumes these
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