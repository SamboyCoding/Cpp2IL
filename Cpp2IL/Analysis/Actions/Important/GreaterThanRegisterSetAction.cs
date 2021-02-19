using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class GreaterThanRegisterSetAction : ConditionalRegisterSetAction
    {
        public GreaterThanRegisterSetAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
        }

        protected override string GetTextSummaryCondition()
        {
            return $"{_associatedCompare?.ArgumentOne?.GetPseudocodeRepresentation()} is greater than {_associatedCompare?.ArgumentTwo?.GetPseudocodeRepresentation()}";
        }

        protected override string GetPseudocodeCondition()
        {
            return $"{_associatedCompare?.ArgumentOne?.GetPseudocodeRepresentation()} > {_associatedCompare?.ArgumentTwo?.GetPseudocodeRepresentation()}";
        }
    }
}