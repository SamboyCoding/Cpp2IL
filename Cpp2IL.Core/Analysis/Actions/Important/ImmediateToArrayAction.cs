using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class ImmediateToArrayAction : BaseAction<Instruction>
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
            var ret = new List<Mono.Cecil.Cil.Instruction>();

            if (_arrayLocal == null)
                throw new TaintedInstructionException("Array couldn't be resolved");

            //stelem.ref: Load array, load index, load value, pop all 3
            
            //Load array
            ret.AddRange(_arrayLocal.GetILToLoad(context, processor));
            
            //Load index
            ret.Add(processor.Create(OpCodes.Ldc_I4, _offset));
            
            //Load value
            ret.Add(processor.Create(OpCodes.Ldc_I8, _immediateValue));
            
            //Pop all 3
            ret.Add(processor.Create(OpCodes.Stelem_Ref));

            return ret.ToArray();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_arrayLocal?.GetPseudocodeRepresentation()}[{_offset}] = {_immediateValue}";
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