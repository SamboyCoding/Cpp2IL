using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64DereferencePointerAction : BaseAction<Arm64Instruction>
    {
        private readonly string _ptrReg;
        private readonly ConstantDefinition _ptr;
        private readonly string _destReg;

        public Arm64DereferencePointerAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            _ptrReg = Utils.GetRegisterNameNew(instruction.MemoryBase()!.Id);
            _ptr = context.GetConstantInReg(_ptrReg)!;
            _destReg = Utils.GetRegisterNameNew(instruction.Details.Operands[0].Register.Id);
            
            context.SetRegContent(_destReg, _ptr);
        }

        public override Instruction[] ToILInstructions(MethodAnalysis<Arm64Instruction> context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Dereferences the pointer in {_ptrReg} (which contains a pointer to {_ptr}) and stores the value in {_destReg}";
        }
    }
}