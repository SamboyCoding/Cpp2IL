using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;

namespace Cpp2IL.Analysis.Actions
{
    public class ConstantToGlobalAction : BaseAction
    {
        private object constantValue;
        private object _theGlobal;
        public ConstantToGlobalAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
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
            
            constantValue = instruction.GetImmediate(1);
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
            return $"Writes the constant {constantValue} to {_theGlobal}";
        }
    }
}