using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class FieldToFpuStackAction : AbstractFieldReadAction<Instruction>
    {
        private readonly LocalDefinition? _localBeingReadFrom;
        private readonly uint _offsetBeingRead;
        private readonly FieldUtils.FieldBeingAccessedData? _fieldRead;
        private readonly LocalDefinition? _localMade;

        public FieldToFpuStackAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            _localBeingReadFrom = context.GetLocalInReg(Utils.Utils.GetRegisterNameNew(instruction.MemoryBase));

            if (_localBeingReadFrom == null) return;

            _offsetBeingRead = instruction.MemoryDisplacement;

            if (_localBeingReadFrom.Type?.Resolve() == null) return;

            _fieldRead = FieldUtils.GetFieldBeingAccessed(_localBeingReadFrom.Type.Resolve(), _offsetBeingRead, true);

            if (_fieldRead == null) return;

            _localMade = context.MakeLocal(_fieldRead.GetFinalType());
            context.FloatingPointStack.Push(_localMade);
        }

        public override string ToTextSummary()
        {
            return $"[!] Reads field {_fieldRead} from {_localBeingReadFrom} and pushes the result to the FPU stack as {_localMade}";
        }
    }
}