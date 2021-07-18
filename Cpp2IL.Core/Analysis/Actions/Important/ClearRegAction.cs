using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
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
            _localMade = context.MakeLocal(Utils.Int32Reference, reg: regCleared, knownInitialValue: 0);
            RegisterDefinedLocalWithoutSideEffects(_localMade);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            return new[]
            {
                processor.Create(OpCodes.Ldc_I4_0),
                processor.Create(OpCodes.Stloc, _localMade.Variable)
            };
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