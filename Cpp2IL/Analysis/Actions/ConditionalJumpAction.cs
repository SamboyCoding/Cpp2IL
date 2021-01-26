using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public abstract class ConditionalJumpAction : BaseAction
    {
        protected ComparisonAction? associatedCompare;
        protected bool isIfStatement;
        protected bool isIfElse;
        protected bool isWhile;
        protected ulong jumpTarget;
        
        protected ConditionalJumpAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            jumpTarget = instruction.NearBranchTarget;

            if (jumpTarget > instruction.NextIP && jumpTarget < context.AbsoluteMethodEnd)
            {
                isIfStatement = true;
                if(!context.IdentifiedJumpDestinationAddresses.Contains(jumpTarget))
                    context.IdentifiedJumpDestinationAddresses.Add(jumpTarget);
            }

            associatedCompare = (ComparisonAction) context.Actions.LastOrDefault(a => a is ComparisonAction);

            if (context.IsThereProbablyAnElseAt(jumpTarget))
            {
                context.RegisterIfElseStatement(instruction.NextIP, jumpTarget, this);
                isIfElse = true;
                context.IndentLevel += 1;
            }

            if (associatedCompare?.IsProbablyWhileLoop() == true)
            {
                isWhile = true;
                context.IndentLevel += 1;
            }
        }

        protected abstract string GetPseudocodeCondition();

        protected abstract string GetTextSummaryCondition();

        public override bool IsImportant()
        {
            return associatedCompare?.unimportantComparison == false && (isIfElse || isWhile);
        }

        protected string GetArgumentOnePseudocodeValue()
        {
            return associatedCompare?.ArgumentOne == null ? "" : associatedCompare.ArgumentOne.GetPseudocodeRepresentation();
        }

        protected string GetArgumentTwoPseudocodeValue()
        {
            return associatedCompare?.ArgumentTwo == null ? "" : associatedCompare.ArgumentTwo.GetPseudocodeRepresentation();
        }
        
        public override string? ToPsuedoCode()
        {
            return isWhile ? $"while {GetPseudocodeCondition()}" : $"if {GetPseudocodeCondition()}";
        }

        public override string ToTextSummary()
        {
            return $"Jumps to 0x{jumpTarget:X}{(isIfStatement ? " (which is an if statement's body)" : "")} if {GetTextSummaryCondition()}\n";
        }

        public override bool PseudocodeNeedsLinebreakBefore()
        {
            return true;
        }
    }
}