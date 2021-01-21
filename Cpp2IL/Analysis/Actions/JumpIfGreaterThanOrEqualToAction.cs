using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class JumpIfGreaterThanOrEqualToAction : BaseAction
    {
        private ComparisonAction? associatedCompare;
        private ulong jumpTarget;
        private bool isIfStatement;
        
        public JumpIfGreaterThanOrEqualToAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            jumpTarget = instruction.NearBranchTarget;

            if (jumpTarget > instruction.NextIP && jumpTarget < context.AbsoluteMethodEnd)
            {
                isIfStatement = true;
                if(!context.IdentifiedIfStatementStarts.Contains(jumpTarget))
                    context.IdentifiedIfStatementStarts.Add(jumpTarget);
            }

            associatedCompare = (ComparisonAction) context.Actions.LastOrDefault(a => a is ComparisonAction);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            if (associatedCompare == null)
                return $"Jumps to 0x{jumpTarget:X}{(isIfStatement ? " (which is an if statement's body)" : "")} if the compare showed that it was greater than or equal";

            return $"Jumps to 0x{jumpTarget:X}{(isIfStatement ? " (which is an if statement's body)" : "")} if {associatedCompare.ArgumentOne} >= {associatedCompare.ArgumentTwo}";
        }
    }
}