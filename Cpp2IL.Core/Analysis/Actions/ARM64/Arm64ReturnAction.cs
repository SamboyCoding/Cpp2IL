using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64ReturnAction : AbstractReturnAction<Arm64Instruction>
    {
        public Arm64ReturnAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            //We've already determined where the return value should go
            var returnValLocation = context.Arm64ReturnValueLocation;

            returnValue = returnValLocation switch
            {
                Arm64ReturnValueLocation.NO_RETURN_VALUE => null,
                Arm64ReturnValueLocation.X0 => context.GetOperandInRegister("x0"),
                Arm64ReturnValueLocation.V0 => context.GetOperandInRegister("v0"),
                Arm64ReturnValueLocation.X0_1 => context.GetOperandInRegister("x0"),
                Arm64ReturnValueLocation.POINTER_R8 => context.GetOperandInRegister("x8"),
                Arm64ReturnValueLocation.POINTER_X0 => context.GetOperandInRegister("x0"),
                Arm64ReturnValueLocation.POINTER_X1 => context.GetOperandInRegister("x1"),
                _ => throw new ArgumentOutOfRangeException()
            };

            if (returnValue is LocalDefinition l)
                RegisterUsedLocal(l);
        }
    }
}