using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64ZeroRegisterToRegisterAction : BaseAction<Arm64Instruction>
    {
        private string _destReg;
        private LocalDefinition _localMade;

        public Arm64ZeroRegisterToRegisterAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            _destReg = Arm64Utils.GetRegisterNameNew(instruction.Details.Operands[0].Register.Id);
            _localMade = context.MakeLocal(MiscUtils.Int64Reference, reg: _destReg, knownInitialValue: 0UL);
            RegisterDefinedLocalWithoutSideEffects(_localMade);
        }

        public override Instruction[] ToILInstructions(MethodAnalysis<Arm64Instruction> context, ILProcessor processor)
        {
            if (_localMade.Variable == null)
                return Array.Empty<Instruction>();
            
            return new[]
            {
                processor.Create(OpCodes.Ldc_I4, 0),
                processor.Create(OpCodes.Stloc, _localMade.Variable)
            };
        }

        public override string? ToPsuedoCode()
        {
            return $"{_localMade.Type} {_localMade.Name} = 0";
        }

        public override string ToTextSummary()
        {
            return $"[!] Writes the value 0 into the register {_destReg}, creating new local {_localMade.Name}";
        }

        public override bool IsImportant() => true;
    }
}