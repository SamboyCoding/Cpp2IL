using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using SharpDisasm;

namespace Cpp2IL.Analysis.Actions
{
    public class LocalToFieldAction : BaseAction
    {
        public LocalDefinition LocalRead;
        public FieldDefinition FieldWritten;

        public LocalToFieldAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            throw new System.NotImplementedException();
        }

        public override string ToTextSummary()
        {
            throw new System.NotImplementedException();
        }
    }
}