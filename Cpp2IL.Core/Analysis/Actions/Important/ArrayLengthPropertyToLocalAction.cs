using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class ArrayLengthPropertyToLocalAction : BaseAction
    {
        private LocalDefinition? _localMade;
        private string? _destReg;
        private LocalDefinition? _memOp;

        public ArrayLengthPropertyToLocalAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var memReg = Utils.GetRegisterNameNew(instruction.MemoryBase);
            _memOp = context.GetLocalInReg(memReg);

            if (_memOp?.Type?.IsArray != true)
                return;

            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            _localMade = context.MakeLocal(Utils.Int32Reference, reg: _destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            return $"System.Int32 {_localMade?.GetPseudocodeRepresentation()} = {_memOp?.GetPseudocodeRepresentation()}.Length";
        }

        public override string ToTextSummary()
        {
            return $"Reads the length of the array \"{_memOp}\" and stores it in new local {_localMade} in register {_destReg}";
        }

        public override bool IsImportant() => true;
    }
}