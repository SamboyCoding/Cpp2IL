using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class JumpIfZeroOrNullAction : ConditionalJumpAction
    {
        private bool nullMode;
        private bool booleanMode;
        
        public JumpIfZeroOrNullAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            if (associatedCompare != null)
            {
                nullMode = associatedCompare.ArgumentOne == associatedCompare.ArgumentTwo;
                booleanMode = nullMode && associatedCompare.ArgumentOne is LocalDefinition local && local.Type?.FullName == "System.Boolean";
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
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
            
            return "the arguments are equal";
        }

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