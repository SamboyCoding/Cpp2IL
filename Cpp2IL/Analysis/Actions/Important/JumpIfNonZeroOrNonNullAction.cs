using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class JumpIfNonZeroOrNonNullAction : ConditionalJumpAction
    {
        private bool nullMode;
        private bool booleanMode;

        public JumpIfNonZeroOrNonNullAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            if (associatedCompare == null) return;
            
            nullMode = associatedCompare.ArgumentOne == associatedCompare.ArgumentTwo;
            booleanMode = nullMode && associatedCompare.ArgumentOne is LocalDefinition local && local.Type?.FullName == "System.Boolean";
        }

        protected override string GetPseudocodeCondition()
        {
            //We have to invert the condition, so in this case we want "is false", "is null", or "is equal"
            if (booleanMode)
                return $"(!{GetArgumentOnePseudocodeValue()})";

            if (nullMode)
                return $"({GetArgumentOnePseudocodeValue()} == null)";

            if (associatedCompare != null)
                return $"({GetArgumentOnePseudocodeValue()} == {GetArgumentTwoPseudocodeValue()})";

            return "(<missing compare>";
        }

        protected override bool OnlyNeedToLoadOneOperand() => nullMode || booleanMode;

        protected override OpCode GetJumpOpcode()
        {
            if(OnlyNeedToLoadOneOperand())
                return OpCodes.Brtrue;
            
            return OpCodes.Bne_Un;
        }

        protected override string GetTextSummaryCondition()
        {
            if(booleanMode)
                return $"{associatedCompare!.ArgumentOne} is true";
            
            if (nullMode)
                return $"{associatedCompare!.ArgumentOne} is not null";
            
            if(associatedCompare != null)
                return $"{associatedCompare.ArgumentOne} != {associatedCompare.ArgumentTwo}";
            
            return "the compare showed the two items were not equal";
        }
    }
}