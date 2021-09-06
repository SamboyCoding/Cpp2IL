using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class EndIfMarkerAction : BaseAction<Instruction>
    {
        private readonly bool _hasElse;
        private ulong _elsePtr;

        public EndIfMarkerAction(MethodAnalysis<Instruction> context, Instruction instruction, bool hasElse) : base(context, instruction)
        {
            _hasElse = hasElse;
            if(hasElse)
                _elsePtr = context.GetAddressOfElseThisIsTheEndOf(instruction.IP);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
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