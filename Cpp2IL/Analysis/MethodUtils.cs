using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Analysis
{
    public class MethodUtils
    {
        public static bool CheckParameters(Il2CppMethodDefinition method, MethodAnalysis context, bool isInstance, [NotNullWhen(true)] out List<IAnalysedOperand>? arguments)
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
                    case ConstantDefinition cons when cons.Type.FullName != parameterData.Type.ToString(): //Constant type mismatch
                    case LocalDefinition local when !Utils.IsManagedTypeAnInstanceOfCppOne(parameterData.Type, local.Type!): //Local type mismatch
                        return false;
                }

                tempArgs.Add(arg);
            }

            if (actualArgs.Any(a => a != null && !context.IsEmptyRegArg(a)))
                return false; //Left over args - it's probably not this one

            arguments = tempArgs;
            return true;
        }
    }
}