using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class ConstantToRegAction : BaseAction
    {
        private readonly bool _mayNotBeAConstant;
        private ulong constantValue;
        private string destReg;
        private IAnalysedOperand dest;

        public ConstantToRegAction(MethodAnalysis context, Instruction instruction, bool mayNotBeAConstant) : base(context, instruction)
        {
            _mayNotBeAConstant = mayNotBeAConstant;
            constantValue = instruction.GetImmediate(1);
            destReg = Utils.GetRegisterNameNew(instruction.Op0Register);

            if (mayNotBeAConstant)
                //Let's be safe and make this a local
                dest = context.MakeLocal(Utils.UInt64Reference, reg: destReg, knownInitialValue: constantValue);
            else
                dest = context.MakeConstant(typeof(ulong), constantValue, constantValue.ToString(), destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            //TODO we'll need a load of some sort.
            return new Mono.Cecil.Cil.Instruction[0];
        }

        public override string? ToPsuedoCode()
        {
            return $"ulong {(dest is ConstantDefinition constant ? constant.Name : ((LocalDefinition) dest).Name)} = {constantValue}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Writes the constant {constantValue} into operand {dest} in register {destReg}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}