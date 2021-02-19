using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class EndIfMarkerAction : BaseAction
    {
        private ulong _elsePtr;

        public EndIfMarkerAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _elsePtr = context.GetAddressOfElseThisIsTheEndOf(instruction.IP);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            return "endif\n";
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