using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class EndIfMarkerAction : BaseAction
    {
        private readonly bool _hasElse;
        private ulong _elsePtr;

        public EndIfMarkerAction(MethodAnalysis context, Instruction instruction, bool hasElse) : base(context, instruction)
        {
            _hasElse = hasElse;
            if(hasElse)
                _elsePtr = context.GetAddressOfElseThisIsTheEndOf(instruction.IP);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            return new Mono.Cecil.Cil.Instruction[0];
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