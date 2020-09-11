using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class ArrayOffsetToLocalAction : BaseAction
    {
        public LocalDefinition ArrayRead;
        public int OffsetRead;
        public LocalDefinition LocalWritten;
        
        public ArrayOffsetToLocalAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
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