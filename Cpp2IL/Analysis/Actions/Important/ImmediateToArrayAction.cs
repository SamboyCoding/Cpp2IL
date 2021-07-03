using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class ImmediateToArrayAction : BaseAction
    {
        private AllocatedArray? _array;
        private int _offset;
        private ulong _immediateValue;
        private LocalDefinition? _arrayLocal;

        public ImmediateToArrayAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var memReg = Utils.GetRegisterNameNew(instruction.MemoryBase);
            _arrayLocal = context.GetLocalInReg(memReg);
            
            _array = _arrayLocal?.KnownInitialValue as AllocatedArray;
            
            if(_array == null)
                return;

            _immediateValue = instruction.GetImmediate(1);

            _offset = (int) (instruction.MemoryDisplacement32 - Il2CppArrayUtils.FirstItemOffset) / Utils.GetPointerSizeBytes();

            _array.KnownValuesAtOffsets[_offset] = _immediateValue;
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
            return $"[!] Writes {_immediateValue} (immediate ulong) into the array {_arrayLocal?.GetPseudocodeRepresentation()} at index {_offset}.\n";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}