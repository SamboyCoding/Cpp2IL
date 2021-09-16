using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64JumpIfZeroOrNullAction : BaseArm64ConditionalJumpAction
    {
        private bool nullMode;
        private bool booleanMode;
        
        public Arm64JumpIfZeroOrNullAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction, int destinationIndex) : base(context, instruction, destinationIndex)
        {
            if (associatedCompare == null) return;
            
            nullMode = associatedCompare.ArgumentOne == associatedCompare.ArgumentTwo || associatedCompare.ArgumentTwo == null;
            booleanMode = nullMode && associatedCompare.ArgumentOne is LocalDefinition { Type: { FullName: "System.Boolean" } };
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