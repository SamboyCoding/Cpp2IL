using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64OrZeroAndImmAction : BaseAction<Arm64Instruction>
    {
        private readonly string _destReg;
        private readonly long _immValue;
        private readonly LocalDefinition _localMade;

        public Arm64OrZeroAndImmAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            _destReg = Arm64Utils.GetRegisterNameNew(instruction.Details.Operands[0].Register.Id);
            _immValue = instruction.Details.Operands[2].Immediate;

            _localMade = context.MakeLocal(TypeDefinitions.Int64, reg: _destReg, knownInitialValue: _immValue);
            RegisterDefinedLocalWithoutSideEffects(_localMade);
        }

        public override Instruction[] ToILInstructions(MethodAnalysis<Arm64Instruction> context, ILProcessor processor)
        {
            if (_localMade.Variable == null)
                return Array.Empty<Instruction>();
            
            return new[]
            {
                processor.Create(OpCodes.Ldc_I4, (int) _immValue),
                processor.Create(OpCodes.Stloc, _localMade.Variable)
            };
        }

        public override string? ToPsuedoCode()
        {
            return $"{_localMade.Type} {_localMade.Name} = {_immValue}";
        }

        public override string ToTextSummary()
        {
            return $"Creates new local {_localMade} in {_destReg} by ORing 0 with {_immValue}";
        }

        public override bool IsImportant() => true;
    }
}