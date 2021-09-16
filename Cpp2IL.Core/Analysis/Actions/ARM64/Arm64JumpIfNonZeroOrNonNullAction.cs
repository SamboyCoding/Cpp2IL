using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64JumpIfNonZeroOrNonNullAction : BaseArm64ConditionalJumpAction
    {
        private bool nullMode;
        private bool booleanMode;

        public Arm64JumpIfNonZeroOrNonNullAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction, int destinationIndex) : base(context, instruction, destinationIndex)
        {
            if (associatedCompare == null) return;
            
            nullMode = associatedCompare.ArgumentOne == associatedCompare.ArgumentTwo  || associatedCompare.ArgumentTwo == null;;
            booleanMode = nullMode && associatedCompare.ArgumentOne is LocalDefinition { Type: { FullName: "System.Boolean" } };
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

            return "(<missing compare>)";
        }
        
        protected override string GetInvertedPseudocodeConditionForGotos()
        {
            if (booleanMode)
                return $"({GetArgumentOnePseudocodeValue()})";

            if (nullMode)
                return $"({GetArgumentOnePseudocodeValue()} != null)";

            if (associatedCompare != null)
                return $"({GetArgumentOnePseudocodeValue()} != {GetArgumentTwoPseudocodeValue()})";

            return "(<missing compare>)";
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