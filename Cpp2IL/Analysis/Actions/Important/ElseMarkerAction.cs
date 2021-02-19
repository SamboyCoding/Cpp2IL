using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class ElseMarkerAction : BaseAction
    {
        private ulong _ifPtr;

        public ElseMarkerAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _ifPtr = context.GetAddressOfAssociatedIfForThisElse(instruction.IP);
            context.IndentLevel += 1; //For else block
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            return new Mono.Cecil.Cil.Instruction[0];
        }

        public override string? ToPsuedoCode()
        {
            return "else";
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