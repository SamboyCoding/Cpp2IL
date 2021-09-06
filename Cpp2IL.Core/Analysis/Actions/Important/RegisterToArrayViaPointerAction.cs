using System.Collections.Generic;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class RegisterToArrayViaPointerAction : BaseAction<Instruction>
    {
        private Il2CppArrayOffsetPointer<Instruction>? _arrayPointer;
        private IAnalysedOperand<Instruction>? _sourceOp;

        public RegisterToArrayViaPointerAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            var memReg = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var arrayPointerCons = context.GetConstantInReg(memReg);
            
            _arrayPointer = arrayPointerCons?.Value as Il2CppArrayOffsetPointer<Instruction>;
            
            if(_arrayPointer == null)
                return;

            var sourceReg = Utils.GetRegisterNameNew(instruction.Op1Register);
            _sourceOp = context.GetOperandInRegister(sourceReg);
            
            if(!(_arrayPointer.Array.KnownInitialValue is AllocatedArray array))
                return;

            array.KnownValuesAtOffsets[_arrayPointer.Offset] = _sourceOp switch
            {
                LocalDefinition<Instruction> loc => loc.KnownInitialValue,
                ConstantDefinition<Instruction> cons => cons.Value,
                _ => null
            };
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            var ret = new List<Mono.Cecil.Cil.Instruction>();

            if (_arrayPointer == null)
                throw new TaintedInstructionException("Array couldn't be resolved");

            //stelem.ref: Load array, load index, load value, pop all 3
            
            //Load array
            ret.AddRange(_arrayPointer.Array.GetILToLoad(context, processor));
            
            //Load index
            ret.Add(processor.Create(OpCodes.Ldc_I4, _arrayPointer.Offset));
            
            //Load value
            ret.Add(processor.Create(OpCodes.Ldc_I4, _sourceOp?.GetILToLoad(context, processor)));
            
            //Pop all 3
            ret.Add(processor.Create(OpCodes.Stelem_Ref));

            return ret.ToArray();
        }

        public override string ToPsuedoCode()
        {
            return $"{_arrayPointer?.Array.GetPseudocodeRepresentation()}[{_arrayPointer?.Offset}] = {_sourceOp?.GetPseudocodeRepresentation()}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Writes {_sourceOp?.GetPseudocodeRepresentation()} into the array {_arrayPointer?.Array?.GetPseudocodeRepresentation()} at index {_arrayPointer?.Offset} via a pointer.\n";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}