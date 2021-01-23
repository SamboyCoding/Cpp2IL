using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class JumpIfZeroOrNullAction : BaseAction
    {
        private ComparisonAction? associatedCompare;
        private bool nullMode;
        private bool booleanMode;
        private ulong jumpTarget;
        private bool isIfStatement;
        
        public JumpIfZeroOrNullAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            jumpTarget = instruction.NearBranchTarget;
            
            if (jumpTarget > instruction.NextIP && jumpTarget < context.AbsoluteMethodEnd)
            {
                isIfStatement = true;
                if(!context.IdentifiedJumpDestinationAddresses.Contains(jumpTarget))
                    context.IdentifiedJumpDestinationAddresses.Add(jumpTarget);
            }

            associatedCompare = (ComparisonAction) context.Actions.LastOrDefault(a => a is ComparisonAction);
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

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            if(booleanMode)
                return $"Jumps to 0x{jumpTarget:X}{(isIfStatement ? " (which is an if statement's body)" : "")} if {associatedCompare!.ArgumentOne} is false\n";
            
            if (nullMode)
                return $"Jumps to 0x{jumpTarget:X}{(isIfStatement ? " (which is an if statement's body)" : "")} if the compare showed it was null\n";
            
            return $"Jumps to 0x{jumpTarget:X}{(isIfStatement ? " (which is an if statement's body)" : "")} if the compare showed the two items were equal\n";
        }
    }
}