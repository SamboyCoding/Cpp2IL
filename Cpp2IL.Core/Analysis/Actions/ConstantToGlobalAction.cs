using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions
{
    public class ConstantToGlobalAction : BaseAction
    {
        private object constantValue;
        private object _theGlobal;
        public ConstantToGlobalAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            var offset = LibCpp2IlMain.Binary.is32Bit ? instruction.MemoryDisplacement64 : instruction.GetRipBasedInstructionMemoryAddress();
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
            return $"Writes the constant {constantValue} to {_theGlobal}";
        }
    }
}