using System.Collections.Generic;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions
{
    public class RegToConstantArrayOffsetAction : BaseAction
    {
        private long _offsetIdx;
        private LocalDefinition? _arrayInMem;
        private IAnalysedOperand? _opRead;
        private TypeReference? _elementType;

        public RegToConstantArrayOffsetAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var memReg = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var relativeOffset = instruction.MemoryDisplacement - Il2CppArrayUtils.FirstItemOffset;
            _offsetIdx = relativeOffset / Utils.GetPointerSizeBytes();

            _arrayInMem = context.GetLocalInReg(memReg);

            if (_arrayInMem?.Type?.IsArray != true)
                return;

            _elementType = ((ArrayType) _arrayInMem.Type).ElementType;

            var regRead = Utils.GetRegisterNameNew(instruction.Op1Register);
            _opRead = context.GetOperandInRegister(regRead);
            
            if(_arrayInMem != null)
                RegisterUsedLocal(_arrayInMem);
            
            if(_opRead is LocalDefinition l)
                RegisterUsedLocal(l);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            if (_offsetIdx < 0 || _arrayInMem == null || _opRead == null)
                throw new TaintedInstructionException();

            var ret = new List<Mono.Cecil.Cil.Instruction>();
            
            //Load array
            ret.AddRange(_arrayInMem.GetILToLoad(context, processor));
            
            //Load offset
            ret.Add(processor.Create(OpCodes.Ldc_I4, (int) _offsetIdx));
            
            //Load value
            ret.AddRange(_opRead.GetILToLoad(context, processor));
            
            //Store in array
            ret.Add(processor.Create(OpCodes.Stelem_Any, _elementType));

            return ret.ToArray();
        }

        public override string? ToPsuedoCode()
        {
            return $"{_arrayInMem?.Name}[{_offsetIdx}] = {_opRead?.GetPseudocodeRepresentation()}";
        }

        public override string ToTextSummary()
        {
            return $"Sets the value at offset {_offsetIdx} in array {_arrayInMem?.Name} to {_opRead}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}