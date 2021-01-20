using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class PushRegisterAction : BaseAction
    {
        public IAnalysedOperand? whatIsPushed;
        public string regPushedFrom;
        
        public PushRegisterAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            regPushedFrom = Utils.GetRegisterNameNew(instruction.Op0Register);
            whatIsPushed = context.GetOperandInRegister(regPushedFrom);
            
            if(whatIsPushed != null)
                context.Stack.Push(whatIsPushed);
            else
                context.PushEmptyStackFrames(1);
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
            if(whatIsPushed != null)
                return $"Pushes {whatIsPushed} from register {regPushedFrom} to the stack";

            return $"Saves the content of {regPushedFrom} to the stack";
        }
    }
}