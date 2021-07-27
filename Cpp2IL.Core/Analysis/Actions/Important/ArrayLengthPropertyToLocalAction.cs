using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class ArrayLengthPropertyToLocalAction : BaseAction
    {
        public LocalDefinition? LocalMade;
        public LocalDefinition? TheArray;
        private string? _destReg;

        public ArrayLengthPropertyToLocalAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var memReg = Utils.GetRegisterNameNew(instruction.MemoryBase);
            TheArray = context.GetLocalInReg(memReg);

            if (TheArray?.Type?.IsArray != true)
                return;

            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            LocalMade = context.MakeLocal(Utils.Int32Reference, reg: _destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            return $"System.Int32 {LocalMade?.GetPseudocodeRepresentation()} = {TheArray?.GetPseudocodeRepresentation()}.Length";
        }

        public override string ToTextSummary()
        {
            return $"Reads the length of the array \"{TheArray}\" and stores it in new local {LocalMade} in register {_destReg}";
        }

        public override bool IsImportant() => true;
    }
}