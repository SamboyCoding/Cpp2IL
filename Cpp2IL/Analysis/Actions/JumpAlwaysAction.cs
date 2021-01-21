using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class JumpAlwaysAction : BaseAction
    {
        private ulong jumpTarget;
        private bool isIfStatement;
        
        public JumpAlwaysAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            jumpTarget = instruction.NearBranchTarget;
            
            if (jumpTarget > instruction.NextIP && jumpTarget < context.AbsoluteMethodEnd)
            {
                isIfStatement = true;
                if(!context.IdentifiedIfStatementStarts.Contains(jumpTarget))
                    context.IdentifiedIfStatementStarts.Add(jumpTarget);
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
            return $"Jumps to 0x{jumpTarget:X}{(isIfStatement ? " (which is an function-local jump destination)" : "")}\n";
        }
    }
}