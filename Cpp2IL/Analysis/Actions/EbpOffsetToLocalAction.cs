using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;

namespace Cpp2IL.Analysis.Actions
{
    public class EbpOffsetToLocalAction : BaseAction
    {
        private LocalDefinition localBeingRead;
        private string _destReg;

        public EbpOffsetToLocalAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            localBeingRead = StackPointerUtils.GetLocalReferencedByEBPRead(context, instruction);

            if (localBeingRead == null) return;
            
            _destReg = Utils.GetRegisterNameNew(instruction.Op0Register);
            context.SetRegContent(_destReg, localBeingRead);
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
            return $"Copies EBP-Param {localBeingRead} to register {_destReg}";
        }
    }
}