using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
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

            var is32BitInteger = instruction.Op0Register.IsGPR32();

            if (is32BitInteger)
                constantValue &= 0xFFFFFFFF;

            if (mayNotBeAConstant)
            {
                //Let's be safe and make this a local
                if (is32BitInteger)
                    dest = context.MakeLocal(Utils.UInt32Reference, reg: destReg, knownInitialValue: (uint) constantValue);
                else
                    dest = context.MakeLocal(Utils.UInt64Reference, reg: destReg, knownInitialValue: constantValue);
                RegisterDefinedLocalWithoutSideEffects((LocalDefinition) dest);
            }
            else
            {
                if (is32BitInteger)
                    dest = context.MakeConstant(typeof(uint), (uint) constantValue, constantValue.ToString(), destReg);
                else
                    dest = context.MakeConstant(typeof(ulong), constantValue, constantValue.ToString(), destReg);
            }
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            //TODO we'll need a load of some sort.
            return new Mono.Cecil.Cil.Instruction[0];
        }

        public override string? ToPsuedoCode()
        {
            return $"{Utils.Int64Reference} {(dest is ConstantDefinition constant ? constant.Name : ((LocalDefinition) dest).Name)} = {(constantValue > 1024 ? $"0x{constantValue:X}" : $"{constantValue}")}";
        }

        public override string ToTextSummary()
        {
            return $"[!] Writes the constant 0x{constantValue:X} into operand {dest} in register {destReg}";
        }

        public override bool IsImportant()
        {
            if (dest is ConstantDefinition constantDefinition && !constantDefinition.GetPseudocodeRepresentation().StartsWith("{"))
                return false;

            return true;
        }
    }
}