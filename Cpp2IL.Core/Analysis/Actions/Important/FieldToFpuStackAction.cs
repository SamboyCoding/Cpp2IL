using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class FieldToFpuStackAction : BaseAction<Instruction>
    {
        private readonly LocalDefinition? _localBeingReadFrom;
        private readonly uint _offsetBeingRead;
        private readonly FieldUtils.FieldBeingAccessedData? _fieldRead;
        private readonly LocalDefinition? _localMade;

        public FieldToFpuStackAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _localBeingReadFrom = context.GetLocalInReg(Utils.GetRegisterNameNew(instruction.MemoryBase));

            if (_localBeingReadFrom == null) return;

            _offsetBeingRead = instruction.MemoryDisplacement;

            if (_localBeingReadFrom.Type?.Resolve() == null) return;

            _fieldRead = FieldUtils.GetFieldBeingAccessed(_localBeingReadFrom.Type.Resolve(), _offsetBeingRead, true);

            if (_fieldRead == null) return;

            _localMade = context.MakeLocal(_fieldRead.GetFinalType());
            context.FloatingPointStack.Push(_localMade);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_localMade?.Type} {_localMade?.Name} = {_localBeingReadFrom?.Name}.{_fieldRead}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Reads field {_fieldRead} from {_localBeingReadFrom} and pushes the result to the FPU stack as {_localMade}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}