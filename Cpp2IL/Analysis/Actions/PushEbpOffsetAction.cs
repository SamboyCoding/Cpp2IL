using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;

namespace Cpp2IL.Analysis.Actions
{
    public class PushEbpOffsetAction : BaseAction
    {
        private LocalDefinition localBeingPushed;
        public PushEbpOffsetAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            localBeingPushed = StackPointerUtils.GetLocalReferencedByEBPRead(context, instruction);
            
            if(localBeingPushed != null)
                context.Stack.Push(localBeingPushed);
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
            return $"Pushes {localBeingPushed} to the stack";
        }
    }
}