using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class IntegerDivisionShiftStepAction : BaseAction<Instruction>
    {
        private string? _regBeingShifted;
        private bool _isUpperHalf;
        private ConstantDefinition? _constantInReg;
        private IntegerDivisionInProgress<Instruction>? _intDivision;
        private int _fullShiftValue;
        private long _divisor;
        private bool _potentiallyWrong;
        private LocalDefinition? _localMade;

        //The right-shift after what looks like the start of an integer division setup (i.e. IMUL reg when reg has an int, and rax contains a large constant)
        public IntegerDivisionShiftStepAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            //This may need expanding on / improving
            _isUpperHalf = instruction.Op0Register.IsGPR32() && instruction.Op0Register == Register.EDX;

            _regBeingShifted = Utils.GetRegisterNameNew(instruction.Op0Register);
            _constantInReg = context.GetConstantInReg(_regBeingShifted);

            _intDivision = _constantInReg?.Value as IntegerDivisionInProgress<Instruction>;
            
            if(_intDivision == null)
                return;

            _intDivision.TookTopHalf = _isUpperHalf;
            _intDivision.ShiftCount = (int) instruction.GetImmediate(1);
            _intDivision.IsComplete = true;

            _fullShiftValue = _isUpperHalf ? 32 + _intDivision.ShiftCount : _intDivision.ShiftCount;

            var divisorRaw = Math.Pow(2, _fullShiftValue) / _intDivision.MultipliedBy;
            _divisor = (long) Math.Round(divisorRaw);

            if (Math.Abs(_divisor - divisorRaw) > 0.001)
            {
                _potentiallyWrong = true;
            }

            _localMade = context.MakeLocal(Utils.UInt64Reference, reg: _regBeingShifted);
            RegisterUsedLocal(_localMade, context);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            if (_divisor != 0)
                return $"{_localMade?.Type?.FullName} {_localMade?.Name} = ({_localMade?.Type?.FullName}) {_intDivision?.OriginalValue?.GetPseudocodeRepresentation()} / {_divisor}";

            return "//Looks like there should be an integer division here but the divisor couldn't be resolved.";
        }

        public override string ToTextSummary()
        {
            return $"[!] Performs the second step of an integer division by shifting ({_intDivision?.OriginalValue?.GetPseudocodeRepresentation()} * {_intDivision?.MultipliedBy}) right {(_fullShiftValue)} places." +
                   $" Effectively performes a division of {_intDivision?.OriginalValue?.GetPseudocodeRepresentation()} by {_divisor}." +
                   (_potentiallyWrong ? " WARNING: This could be wrong. Divisor didn't come out as a neat number. " : "") +
                   $"The result is stored in new local {_localMade?.Name} in register {_regBeingShifted}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}