using Cpp2IL.Core.Analysis.Actions.x86;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class JumpIfZeroOrNullAction : BaseX86ConditionalJumpAction
    {
        private bool nullMode;
        private bool booleanMode;
        
        public JumpIfZeroOrNullAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            if (associatedCompare != null)
            {
                nullMode = associatedCompare.ArgumentOne == associatedCompare.ArgumentTwo;
                booleanMode = nullMode && associatedCompare.ArgumentOne is LocalDefinition<Instruction> local && local.Type?.FullName == "System.Boolean";
            }
        }

        protected override string GetPseudocodeCondition()
        {
            //Have to invert
            if(booleanMode)
                return $"({GetArgumentOnePseudocodeValue()})";
            
            if (nullMode)
                return $"({GetArgumentOnePseudocodeValue()} != null)";

            if (associatedCompare != null)
                return $"({GetArgumentOnePseudocodeValue()} != {GetArgumentTwoPseudocodeValue()})";
            
            return "the arguments are not equal";
        }
        
        protected override string GetInvertedPseudocodeConditionForGotos()
        {
            if (booleanMode)
                return $"(!{GetArgumentOnePseudocodeValue()})";

            if (nullMode)
                return $"({GetArgumentOnePseudocodeValue()} == null)";

            if (associatedCompare != null)
                return $"({GetArgumentOnePseudocodeValue()} == {GetArgumentTwoPseudocodeValue()})";

            return "(<missing compare>)";
        }

        protected override OpCode GetJumpOpcode()
        {
            if(OnlyNeedToLoadOneOperand())
                return OpCodes.Brfalse;
            
            return OpCodes.Beq;
        }

        protected override bool OnlyNeedToLoadOneOperand() => booleanMode || nullMode;

        protected override string GetTextSummaryCondition()
        {
            if(booleanMode)
                return $"{GetArgumentOnePseudocodeValue()} is false";
            
            if (nullMode)
                return $"{GetArgumentOnePseudocodeValue()} is null";

            if (associatedCompare != null)
                return $"{GetArgumentOnePseudocodeValue()} equals {GetArgumentTwoPseudocodeValue()}";
            
            return "the arguments are equal";
        }
    }
}