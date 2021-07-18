using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class AddConstantToRegAction : BaseAction
    {
        private string _regBeingAddedTo;
        private LocalDefinition? _valueInReg;
        private ulong _constantBeingAdded;

        public AddConstantToRegAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _regBeingAddedTo = Utils.GetRegisterNameNew(instruction.Op0Register);
            _valueInReg = context.GetLocalInReg(_regBeingAddedTo);
            
            //Handle INC instructions here too.
            _constantBeingAdded = instruction.Mnemonic == Mnemonic.Inc ? 1 : instruction.GetImmediate(1); 

            if (_valueInReg?.Type == null) return;

            if (!Utils.IsNumericType(_valueInReg.Type))
            {
                AddComment("Type being added to is non-numeric!");
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_valueInReg?.Name} += {_constantBeingAdded}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Adds {_constantBeingAdded} to the value {_valueInReg}, stored in {_regBeingAddedTo}";
        }
        
        public override bool IsImportant()
        {
            return true;
        }
    }
}