using System;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class FieldToLocalAction : BaseAction
    {
        public FieldDefinition FieldRead;
        public LocalDefinition LocalWritten;

        public FieldToLocalAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new NotImplementedException();
        }

        public override string ToPsuedoCode()
        {
            throw new NotImplementedException();
        }

        public override string ToTextSummary()
        {
            throw new NotImplementedException();
        }
    }
}