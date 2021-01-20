using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;

namespace Cpp2IL.Analysis.Actions
{
    public class PushGlobalAction : BaseAction
    {
        private object _theGlobal;

        public PushGlobalAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var offset = LibCpp2IlMain.ThePe.is32Bit ? instruction.MemoryDisplacement64 : instruction.GetRipBasedInstructionMemoryAddress();
            if (LibCpp2IlMain.GetAnyGlobalByAddress(offset) is { } globalIdentifier && globalIdentifier.Offset == offset)
            {
                _theGlobal = globalIdentifier;
            }
            else
            {
                _theGlobal = new UnknownGlobalAddr(offset);
            }
            
            if(_theGlobal != null)
                context.Stack.Push(context.MakeConstant(_theGlobal.GetType(), _theGlobal));
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
            return $"Pushes {_theGlobal} onto the stack";
        }
    }
}