using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class GoToDestinationMarker : BaseAction<Instruction>
    {
        public GoToDestinationMarker(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            return new Mono.Cecil.Cil.Instruction[0];
        }

        public override string? ToPsuedoCode()
        {
            return $"INSN_{AssociatedInstruction.IP:X}:";
        }

        public override string ToTextSummary()
        {
            return "";
        }

        public override bool PseudocodeNeedsLinebreakBefore()
        {
            return true;
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}