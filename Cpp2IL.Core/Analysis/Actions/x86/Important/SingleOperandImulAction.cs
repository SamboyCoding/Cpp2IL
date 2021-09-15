using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class SingleOperandImulAction : BaseAction<Instruction>
    {
        private LocalDefinition? _returnedLocal;
        private ConstantDefinition? _intDivisionConstant;
        private IAnalysedOperand? _secondOperand;
        private IAnalysedOperand? _firstOperand;

        public SingleOperandImulAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            //IMUL reg
            // = multiply rax by reg and store in rax

            _firstOperand = context.GetOperandInRegister("rax");
            var secondOpRegName = Utils.GetRegisterNameNew(instruction.Op0Register);
            _secondOperand = context.GetOperandInRegister(secondOpRegName);

            //If this is an integer division, rax usually has the constant.
            ulong firstOpConstant = 0;
            if (_firstOperand is ConstantDefinition {Value: { } val} && Utils.TryCoerceToUlong(val, out var constUlong))
                firstOpConstant = constUlong;
            else if (_firstOperand is LocalDefinition {KnownInitialValue: { } val2} && Utils.TryCoerceToUlong(val2, out var localUlong))
                firstOpConstant = localUlong;

            if (firstOpConstant != 0 && _secondOperand != null)
            {
                _intDivisionConstant = context.MakeConstant(typeof(IntegerDivisionInProgress<Instruction>), new IntegerDivisionInProgress<Instruction>(_secondOperand, firstOpConstant), reg: "rax");
                context.SetRegContent("rdx", _intDivisionConstant);
                return;
            }

            //TODO technically this goes into eax for the lower 32 bits and edx for the upper.
            _returnedLocal = context.MakeLocal(Utils.UInt64Reference, reg: "rax");
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_returnedLocal?.Type?.FullName} {_returnedLocal?.Name} = {_firstOperand?.GetPseudocodeRepresentation()} * {_secondOperand?.GetPseudocodeRepresentation()}";
        }

        public override string ToTextSummary()
        {
            if (!IsImportant())
            {
                //Integer division mode
                return $"Performs the first step of an integer division by multiplying {_firstOperand} by {_secondOperand}";
            }
            
            return $"[!] Multiplies {_firstOperand} and {_secondOperand} and stores the result in new local {_returnedLocal} in register rax";
        }

        public override bool IsImportant()
        {
            return (_intDivisionConstant?.Value as IntegerDivisionInProgress<Instruction>)?.IsComplete != true;
        }
    }
}