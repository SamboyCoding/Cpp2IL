using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class ReturnFromFunctionAction : AbstractReturnAction<Instruction>
    {
        public ReturnFromFunctionAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var returnType = context.ReturnType;
            returnValue = returnType.ShouldBeInFloatingPointRegister() ? context.GetOperandInRegister("xmm0") : context.GetOperandInRegister("rax");

            if (returnValue is LocalDefinition l)
                RegisterUsedLocal(l, context);
        }
    }
}