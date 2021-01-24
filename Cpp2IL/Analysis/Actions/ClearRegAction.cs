using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class ClearRegAction : BaseAction
    {
        private string regCleared;
        private LocalDefinition _localMade;

        public ClearRegAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            regCleared = Utils.GetRegisterNameNew(instruction.Op0Register);
            // context.ZeroRegister(regCleared);
            //We make this a local and clean up unused ones in post-processing
            _localMade = context.MakeLocal(Utils.Int32Reference, reg: regCleared);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            return $"ulong {_localMade.Name} = 0";
        }

        public override string ToTextSummary()
        {
            return $"Clears register {regCleared}, yielding zero-local {_localMade}";
        }

        public override bool IsImportant()
        {
            return true;
        }
    }
}