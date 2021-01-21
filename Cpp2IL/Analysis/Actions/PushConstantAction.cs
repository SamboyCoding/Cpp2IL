using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class PushConstantAction : BaseAction
    {
        private ulong _constant;

        public PushConstantAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            _constant = instruction.GetImmediate(0);
            context.Stack.Push(context.MakeConstant(typeof(ulong), _constant));
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
            return $"Pushes the constant value {_constant} to the stack";
        }
    }
}