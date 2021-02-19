using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class JumpBackAction : BaseAction
    {
        private ulong jumpTarget;

        public JumpBackAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            jumpTarget = instruction.NearBranchTarget;
            
            context.ProbableLoopStarts.Add(jumpTarget);
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
            return $"Jumps to 0x{jumpTarget:X} - which is still in this function, but further up. Probably indicative that this is a loop.";
        }
    }
}