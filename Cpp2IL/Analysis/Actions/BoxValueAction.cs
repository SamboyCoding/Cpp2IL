using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class BoxValueAction : BaseAction
    {
        private TypeDefinition? destinationType;
        private IAnalysedOperand primitiveObject;
        
        public BoxValueAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
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
            return $"Boxes a cpp primitive value {primitiveObject} to managed type {destinationType?.FullName}";
        }
    }
}