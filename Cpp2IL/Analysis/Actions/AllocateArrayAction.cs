using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class AllocateArrayAction : BaseAction
    {
        private int sizeAllocated;
        private TypeDefinition? arrayType;
        
        public AllocateArrayAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
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
            return $"Allocates an array of {arrayType?.FullName} of size {sizeAllocated}";
        }
    }
}