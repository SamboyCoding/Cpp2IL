using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class FpuStackLocalToRbpOffsetAction : BaseAction<Instruction>
    {
        private readonly int _slotNum;
        private readonly LocalDefinition? _localBeingPopped;

        public FpuStackLocalToRbpOffsetAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            if (!context.FloatingPointStack.TryPeek(out var stackContent) || !(stackContent is LocalDefinition local))
                return;

            _localBeingPopped = local;
            _slotNum = StackPointerUtils.SaveLocalToStack(context, instruction, local);
            context.FloatingPointStack.Pop();
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            if(_slotNum < 0)
                return $"Pops {_localBeingPopped} from the FPU stack and copies it to the reserved local storage space on the stack, slot {-_slotNum}";
            
            return $"Pops {_localBeingPopped} from the FPU stack and copies it to the reserved *parameter* storage space on the stack, slot {_slotNum}";
        }
    }
}