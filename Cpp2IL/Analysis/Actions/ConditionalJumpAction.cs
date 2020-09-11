using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class ConditionalJumpAction : BaseAction
    {
        public ComparisonAction AssociatedCompare;
        public ConditionType Type;
        
        public ConditionalJumpAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            throw new System.NotImplementedException();
        }

        public enum ConditionType
        {
            LTE, LT, GTE, GT, EQ, NEQ, ISNULL, NONNULL
        } 
    }
}