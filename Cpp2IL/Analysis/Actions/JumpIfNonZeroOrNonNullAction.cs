using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class JumpIfNonZeroOrNonNullAction : BaseAction
    {
        private ComparisonAction? associatedCompare;
        private bool nullMode;
        private ulong jumpTarget;
        private bool isIfStatement;
        
        public JumpIfNonZeroOrNonNullAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            jumpTarget = instruction.NearBranchTarget;

            if (jumpTarget > instruction.NextIP && jumpTarget < context.AbsoluteMethodEnd)
            {
                isIfStatement = true;
                if(!context.IdentifiedIfStatementStarts.Contains(jumpTarget))
                    context.IdentifiedIfStatementStarts.Add(jumpTarget);
            }

            associatedCompare = (ComparisonAction) context.Actions.LastOrDefault(a => a is ComparisonAction);
            if(associatedCompare != null)
                nullMode = associatedCompare.ArgumentOne == associatedCompare.ArgumentTwo;
            
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
            if (nullMode)
                return $"Jumps to 0x{jumpTarget:X}{(isIfStatement ? " (which is an if statement's body)" : "")} if the compare showed it was non-null\n";
            
            return $"Jumps to 0x{jumpTarget:X}{(isIfStatement ? " (which is an if statement's body)" : "")} if the compare showed the two items were not equal\n";
        }
    }
}