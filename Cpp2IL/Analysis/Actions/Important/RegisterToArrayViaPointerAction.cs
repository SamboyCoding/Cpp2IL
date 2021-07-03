using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public class RegisterToArrayViaPointerAction : BaseAction
    {
        private Il2CppArrayOffsetPointer? _arrayPointer;
        private IAnalysedOperand? _sourceOp;

        public RegisterToArrayViaPointerAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var memReg = Utils.GetRegisterNameNew(instruction.MemoryBase);
            var arrayPointerCons = context.GetConstantInReg(memReg);
            
            _arrayPointer = arrayPointerCons?.Value as Il2CppArrayOffsetPointer;
            
            if(_arrayPointer == null)
                return;

            var sourceReg = Utils.GetRegisterNameNew(instruction.Op1Register);
            _sourceOp = context.GetOperandInRegister(sourceReg);
            
            if(!(_arrayPointer.Array.KnownInitialValue is AllocatedArray array))
                return;

            array.KnownValuesAtOffsets[_arrayPointer.Offset] = _sourceOp switch
            {
                LocalDefinition loc => loc.KnownInitialValue,
                ConstantDefinition cons => cons.Value,
                _ => null
            };
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
            return $"[!] Writes {_sourceOp?.GetPseudocodeRepresentation()} into the array {_arrayPointer?.Array?.GetPseudocodeRepresentation()} at index {_arrayPointer?.Offset} via a pointer.\n";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}