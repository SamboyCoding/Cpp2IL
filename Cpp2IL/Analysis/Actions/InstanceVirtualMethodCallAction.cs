using System.Collections.Generic;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil;
using SharpDisasm;

namespace Cpp2IL.Analysis.Actions
{
    public class InstanceVirtualMethodCallAction : BaseAction
    {
        public LocalDefinition CalledOn;
        public MethodDefinition Called;
        public List<IAnalysedOperand> Arguments;

        public InstanceVirtualMethodCallAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
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