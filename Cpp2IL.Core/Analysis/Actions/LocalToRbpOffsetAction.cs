using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class LocalToRbpOffsetAction : BaseAction<Instruction>
    {
        private readonly LocalDefinition? _localBeingRead;
        private readonly string _regBeingRead;
        private readonly int _slotNum;

        public LocalToRbpOffsetAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _regBeingRead = Utils.GetRegisterNameNew(instruction.Op1Register);
            _localBeingRead = context.GetLocalInReg(_regBeingRead);

            if (_localBeingRead == null) return;
            
            _slotNum = StackPointerUtils.SaveLocalToStack(context, instruction, _localBeingRead);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
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