using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class ClearRegAction : BaseAction
    {
        private string regCleared;
        
        public ClearRegAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            regCleared = Utils.GetRegisterNameNew(instruction.Op0Register);
            // context.ZeroRegister(regCleared);
            //We make this a local and clean up unused ones in post-processing
            context.MakeLocal(Utils.Int32Reference, reg: regCleared);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            return $"Clears register {regCleared}";
        }
    }
}