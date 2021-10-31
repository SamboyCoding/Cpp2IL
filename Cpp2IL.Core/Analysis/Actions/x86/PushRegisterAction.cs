using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public class PushRegisterAction : BaseAction<Instruction>
    {
        public IAnalysedOperand? whatIsPushed;
        public string regPushedFrom;
        
        public PushRegisterAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            regPushedFrom = Utils.Utils.GetRegisterNameNew(instruction.Op0Register);
            whatIsPushed = context.GetOperandInRegister(regPushedFrom);

            if(whatIsPushed != null)
                context.Stack.Push(whatIsPushed);
            else
                context.PushEmptyStackFrames(1);

            if (whatIsPushed is LocalDefinition l)
                RegisterUsedLocal(l, context);
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis<Instruction> context, ILProcessor processor)
        {
            throw new System.NotImplementedException();
        }

        public override string? ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            if(whatIsPushed != null)
                return $"Pushes {whatIsPushed} from register {regPushedFrom} to the stack";

            return $"Saves the content of {regPushedFrom} to the stack";
        }
    }
}