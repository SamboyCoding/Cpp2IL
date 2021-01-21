using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;

namespace Cpp2IL.Analysis.Actions
{
    public class LocalToEbpOffsetAction : BaseAction
    {
        private LocalDefinition _localBeingRead;
        private string _regBeingRead;
        private int _slotNum;

        public LocalToEbpOffsetAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _regBeingRead = Utils.GetRegisterNameNew(instruction.Op1Register);
            _localBeingRead = context.GetLocalInReg(_regBeingRead);

            if (_localBeingRead == null) return;
            
            _slotNum = StackPointerUtils.SaveLocalToStack(context, instruction, _localBeingRead);
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
            if(_slotNum < 0)
                return $"Copies local variable {_localBeingRead} from register {_regBeingRead} to the reserved local storage space on the stack, slot {-_slotNum}";
            
            return $"Copies local variable {_localBeingRead} from register {_regBeingRead} to the reserved *parameter* storage space on the stack, slot {_slotNum}";
        }
    }
}