using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public class OrToMinusOneAction : BaseAction<Instruction>
    {
        private string _reg;
        private ConstantDefinition _constantMade;

        //OR reg, 0xFFFFFFFF
        //i.e. set reg to -1
        public OrToMinusOneAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            _reg = Utils.GetRegisterNameNew(instruction.Op0Register);

            _constantMade = context.MakeConstant(typeof(int), -1, reg: _reg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Writes the value -1 into the register {_reg}, yielding a constant {_constantMade.Name}";
        }
    }
}