using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class ConstantToFieldAction: BaseAction
    {
        private object constantValue;
        private IAnalysedOperand instance;
        private FieldDefinition? destinationField;
        
        public ConstantToFieldAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            constantValue = instruction.GetImmediate(1);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(ILProcessor processor)
        {
            //TODO we'll need a load of some sort.
            return new Mono.Cecil.Cil.Instruction[0];
        }

        public override string? ToPsuedoCode()
        {
            return null;
        }

        public override string ToTextSummary()
        {
            return $"[!] Writes the constant {constantValue} into the field {destinationField?.Name} of {instance}";
        }
    }
}