﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;
using Mono.Cecil;

namespace Cpp2IL.Analysis
{
    public class MethodUtils
    {
        public static bool CheckParameters(Il2CppMethodDefinition method, MethodAnalysis context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments, bool failOnLeftoverArgs = true)
        {
            var managedMethod = SharedState.UnmanagedToManagedMethods[method];
            return CheckParameters(managedMethod, context, isInstance, out arguments, failOnLeftoverArgs);
        }
        
        public static bool CheckParameters(MethodDefinition method, MethodAnalysis context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments, bool failOnLeftoverArgs = true)
        {
            return LibCpp2IlMain.ThePe!.is32Bit ? CheckParameters32(method, context, isInstance, out arguments) : CheckParameters64(method, context, isInstance, out arguments, failOnLeftoverArgs);
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

                tempArgs.Add(arg);
            }

            if (failOnLeftoverArgs && actualArgs.Any(a => a != null && !context.IsEmptyRegArg(a)))
                return false; //Left over args - it's probably not this one

            arguments = tempArgs;
            return true;
        }

        private static bool CheckParameters32(MethodDefinition method, MethodAnalysis context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments)
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
                if (!CheckSingleParameter(value, parameterData.ParameterType))
                {
                    RePushStack(listToRePush, context);
                    return false;
                }

                listToRePush.Add(context.Stack.Pop());
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