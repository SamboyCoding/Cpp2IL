using System;
using Cpp2IL.Analysis.ResultModels;
using SharpDisasm;

namespace Cpp2IL.Analysis.Actions
{
    /// <summary>
    /// Action for a simple reg->reg move
    /// </summary>
    public class RegContentCopyAction : BaseAction
    {
        private IAnalysedOperand beingMoved;
        private string originalReg;
        private string newReg;
        
        public RegContentCopyAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            //No-op
            return new Mono.Cecil.Cil.Instruction[0];
        }

        public override string? ToPsuedoCode()
        {
            return null;
        }

        public override string ToTextSummary()
        {
            return $"Copies {beingMoved} from {originalReg} into {newReg}";
        }
    }
}