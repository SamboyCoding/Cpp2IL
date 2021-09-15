using System;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86.Important
{
    public class RegisterToArrayViaPointerAction : AbstractArrayOffsetWriteAction<Instruction>
    {
        private Il2CppArrayOffsetPointer<Instruction>? _arrayPointer;
        private IAnalysedOperand? _sourceOp;

        public RegisterToArrayViaPointerAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var memReg = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var arrayPointerCons = context.GetConstantInReg(memReg);
            
            _arrayPointer = arrayPointerCons?.Value as Il2CppArrayOffsetPointer<Instruction>;
            
            if(_arrayPointer == null)
                return;
            
            TheArray = _arrayPointer.Array;

            var sourceReg = Utils.GetRegisterNameNew(instruction.Op1Register);
            _sourceOp = context.GetOperandInRegister(sourceReg);
            
            if(_arrayPointer.Array.KnownInitialValue is not AllocatedArray array)
                return;

            array.KnownValuesAtOffsets[_arrayPointer.Offset] = _sourceOp switch
            {
                LocalDefinition loc => loc.KnownInitialValue,
                ConstantDefinition cons => cons.Value,
                _ => null
            };
        }

        protected override int GetOffsetWritten() => _arrayPointer!.Offset;

        protected override string? GetPseudocodeValue() => _sourceOp?.GetPseudocodeRepresentation();

        protected override string? GetSummaryValue() => _sourceOp?.ToString();

        protected override Mono.Cecil.Cil.Instruction[] GetInstructionsToLoadValue(MethodAnalysis<Instruction> context, ILProcessor processor) => _sourceOp?.GetILToLoad(context, processor) ?? Array.Empty<Mono.Cecil.Cil.Instruction>();
    }
}