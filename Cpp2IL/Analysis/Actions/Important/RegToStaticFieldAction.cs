using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class RegToStaticFieldAction : BaseAction
    {
        private IAnalysedOperand? _sourceOperand;
        private FieldDefinition? _theField;

        public RegToStaticFieldAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _sourceOperand = context.GetOperandInRegister(Utils.GetRegisterNameNew(instruction.Op1Register));
            var destStaticFieldsPtr = context.GetConstantInReg(Utils.GetRegisterNameNew(instruction.MemoryBase));
            var staticFieldOffset = instruction.MemoryDisplacement;

            if (!(destStaticFieldsPtr?.Value is StaticFieldsPtr staticFieldsPtr)) 
                return;

            _theField = FieldUtils.GetStaticFieldByOffset(staticFieldsPtr, staticFieldOffset);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_theField?.DeclaringType.FullName}.{_theField?.Name} = {_sourceOperand?.GetPseudocodeRepresentation()}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Sets static field {_theField?.DeclaringType.FullName}.{_theField?.Name} to {_sourceOperand}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}