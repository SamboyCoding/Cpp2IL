using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class RegToConstantArrayOffsetAction : AbstractArrayOffsetWriteAction<Instruction>
    {
        private long _offsetIdx;
        private IAnalysedOperand? _opRead;
        private TypeReference? _elementType;

        public RegToConstantArrayOffsetAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var memReg = MiscUtils.GetRegisterNameNew(instruction.MemoryBase);
            var relativeOffset = instruction.MemoryDisplacement - Il2CppArrayUtils.FirstItemOffset;
            _offsetIdx = relativeOffset / MiscUtils.GetPointerSizeBytes();

            TheArray = context.GetLocalInReg(memReg);

            if (TheArray?.Type?.IsArray != true)
                return;

            _elementType = ((ArrayType) TheArray.Type).ElementType;

            var regRead = MiscUtils.GetRegisterNameNew(instruction.Op1Register);
            _opRead = context.GetOperandInRegister(regRead);
            
            if(TheArray != null)
                RegisterUsedLocal(TheArray, context);
            
            if(_opRead is LocalDefinition l)
                RegisterUsedLocal(l, context);
        }

        protected override int GetOffsetWritten() => (int)_offsetIdx;

        protected override string? GetPseudocodeValue() => _opRead?.GetPseudocodeRepresentation();

        protected override string? GetSummaryValue() => _opRead?.ToString();

        protected override Mono.Cecil.Cil.Instruction[] GetInstructionsToLoadValue(MethodAnalysis<Instruction> context, ILProcessor processor) => _opRead?.GetILToLoad(context, processor) ?? Array.Empty<Mono.Cecil.Cil.Instruction>();
    }
}