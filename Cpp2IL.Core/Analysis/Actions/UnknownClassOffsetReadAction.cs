using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class UnknownClassOffsetReadAction : BaseAction<Instruction>
    {
        private uint _offset;

        public UnknownClassOffsetReadAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _offset = instruction.MemoryDisplacement;
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new TaintedInstructionException("Unknown class offset read");
        }

        public override string? ToPsuedoCode()
        {
            return "//UNKNOWN CLASS OFFSET READ HERE";
        }

        public override string ToTextSummary()
        {
            return $"[!!] Reads value at unknown offset {_offset} (0x{_offset:X}) on a klass pointer";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}