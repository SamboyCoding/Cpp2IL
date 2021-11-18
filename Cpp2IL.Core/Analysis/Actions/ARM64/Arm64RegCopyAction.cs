using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone.Arm64;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.ARM64
{
    public class Arm64RegCopyAction : BaseAction<Arm64Instruction>
    {
        private readonly IAnalysedOperand? _whatCopied;
        private readonly string _source;
        private readonly string _dest;

        public Arm64RegCopyAction(MethodAnalysis<Arm64Instruction> context, Arm64Instruction instruction) : base(context, instruction)
        {
            _source = Arm64Utils.GetRegisterNameNew(instruction.Details.Operands[1].Register.Id);
            _dest = Arm64Utils.GetRegisterNameNew(instruction.Details.Operands[0].Register.Id);

            _whatCopied = context.GetOperandInRegister(_source);
            
            if(_whatCopied == null)
                return;
            
            context.SetRegContent(_dest, _whatCopied);
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
            return $"Copies {_whatCopied} from {_source} to {_dest}";
        }
    }
}