using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class EndWhileMarkerAction : BaseAction
    {
        public EndWhileMarkerAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            return "endwhile\n";
        }

        public override string ToTextSummary()
        {
            return "";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}