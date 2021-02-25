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

            var is32BitInteger = instruction.Op0Register >= Register.EAX || instruction.Op0Register <= Register.R15D;

            if (mayNotBeAConstant)
            {
                //Let's be safe and make this a local
                dest = context.MakeLocal(is32BitInteger ? Utils.Int32Reference : Utils.UInt64Reference, reg: destReg, knownInitialValue: constantValue);
                RegisterDefinedLocalWithoutSideEffects((LocalDefinition) dest);
            }
            else
                dest = context.MakeConstant(is32BitInteger ? typeof(int) : typeof(ulong), constantValue, constantValue.ToString(), destReg);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
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
            if (dest is ConstantDefinition constantDefinition && !constantDefinition.GetPseudocodeRepresentation().StartsWith("{"))
                return false;
            
            return true;
        }
    }
}