using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64ImmediateToRegAction : BaseAction<Arm64Instruction>
    {
        private readonly long _immValue;
        private readonly string? _destReg;
        private readonly IAnalysedOperand _dest;

        public Arm64ImmediateToRegAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction, bool mayNotBeAConstant) : base(context, instruction)
        {
            _immValue = instruction.Details.Operands[1].Immediate;
            var destRegId = instruction.Details.Operands[0].Register.Id;
            _destReg = Arm64Utils.GetRegisterNameNew(destRegId);

            var is32BitReg = destRegId < Arm64RegisterId.ARM64_REG_X0;

            if (mayNotBeAConstant)
            {
                if (is32BitReg)
                    _dest = context.MakeLocal(MiscUtils.Int32Reference, reg: _destReg, knownInitialValue: (int) _immValue);
                else
                    _dest = context.MakeLocal(MiscUtils.Int64Reference, reg: _destReg, knownInitialValue: _immValue);
                RegisterDefinedLocalWithoutSideEffects((LocalDefinition)_dest);
            }
            else
            {
                if (is32BitReg)
                    _dest = context.MakeConstant(typeof(int), (int) _immValue, _immValue.ToString(), _destReg);
                else
                    _dest = context.MakeConstant(typeof(long), _immValue, _immValue.ToString(), _destReg);
            }
        }

        public override Instruction[] ToILInstructions(MethodAnalysis<Arm64Instruction> context, ILProcessor processor)
        {
            if (_dest is ConstantDefinition)
                return Array.Empty<Instruction>();

            var local = (LocalDefinition)_dest;

            if (local.Variable is null)
                //stripped
                return Array.Empty<Instruction>();
            
            return new[]
            {
                processor.Create(OpCodes.Ldc_I4, Convert.ToInt32(_immValue)),
                processor.Create(OpCodes.Stloc, local.Variable),
            };
        }

        public override string? ToPsuedoCode()
        {
            return $"{MiscUtils.Int64Reference} {(_dest is ConstantDefinition constant ? constant.Name : ((LocalDefinition)_dest).Name)} = {(_immValue > 1024 ? $"0x{_immValue:X}" : $"{_immValue}")}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Writes the constant 0x{_immValue:X} into operand {_dest} (type UInt64) in register {_destReg}";
        }

        public override bool IsImportant()
        {
            if (_dest is ConstantDefinition constantDefinition && !constantDefinition.GetPseudocodeRepresentation().StartsWith("{"))
                return false;

            return true;
        }
    }
}