using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class ImmediateToStackOffsetAction : BaseAction<Instruction>
    {
        private readonly uint _stackOffset;
        private readonly ulong _sourceImmediate;
        private readonly LocalDefinition? _newLocal;

        public ImmediateToStackOffsetAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            _stackOffset = instruction.MemoryDisplacement32;
            _sourceImmediate = instruction.GetImmediate(1);

            _newLocal = context.MakeLocal(Utils.UInt64Reference, knownInitialValue: _sourceImmediate);
            context.StackStoredLocals[(int) _stackOffset] = _newLocal;
            RegisterUsedLocal(_newLocal, context);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Moves {_sourceImmediate} (immediate ulong value) to the stack at offset {_stackOffset} as a new local {_newLocal?.Name}";
        }
    }
}