using System.Collections.Generic;
using Cpp2IL.Core.Analysis.ResultModels;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class AbstractConditionalJumpAction<T> : BaseAction<T>
    {
        public ulong JumpTarget;
        
        protected AbstractComparisonAction<T>? associatedCompare;
        protected bool IsIfStatement;
        protected bool IsIfElse;
        protected bool IsWhile;
        protected bool IsImplicitNullReferenceException;
        protected bool IsGoto;
        
        protected AbstractConditionalJumpAction(MethodAnalysis<T> context, T associatedInstruction) : base(context, associatedInstruction)
        {
        }
        
        protected abstract string GetPseudocodeCondition();

        protected abstract string GetInvertedPseudocodeConditionForGotos();

        protected abstract string GetTextSummaryCondition();

        public override bool IsImportant()
        {
            return !IsImplicitNullReferenceException && associatedCompare?.UnimportantComparison == false && (IsIfElse || IsWhile || IsIfStatement || IsGoto);
        }

        protected string GetArgumentOnePseudocodeValue()
        {
            return associatedCompare?.ArgumentOne == null ? "" : associatedCompare.ArgumentOne.GetPseudocodeRepresentation();
        }

        protected string GetArgumentTwoPseudocodeValue()
        {
            return associatedCompare?.ArgumentTwo == null ? "" : associatedCompare.ArgumentTwo.GetPseudocodeRepresentation();
        }

        public override string ToPsuedoCode()
        {
            if (IsGoto)
            {
                return $"if {GetInvertedPseudocodeConditionForGotos()}\n" +
                       $"    goto INSN_{JumpTarget:X}\n" +
                       $"endif";
            }
            
            return IsWhile ? $"while {GetPseudocodeCondition()}" : $"if {GetPseudocodeCondition()}";
        }

        public override string ToTextSummary()
        {
            if (IsImplicitNullReferenceException)
                return $"Jumps to 0x{JumpTarget:X} (which throws a NRE) if {GetTextSummaryCondition()}. Implicitly present in managed code, so ignored here.";

            return $"Jumps to 0x{JumpTarget:X}{(IsIfStatement ? " (which is an if statement's body)" : "")} if {GetTextSummaryCondition()}\n";
        }

        protected virtual bool OnlyNeedToLoadOneOperand() => false;

        protected abstract OpCode GetJumpOpcode();

        public override Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor)
        {
            if (associatedCompare?.ArgumentOne == null || associatedCompare.ArgumentTwo == null)
                throw new TaintedInstructionException("One of the arguments is null");

            var ret = new List<Instruction>();
            var dummyTarget = processor.Create(OpCodes.Nop);

            ret.AddRange(associatedCompare.ArgumentOne.GetILToLoad(context, processor));

            if (!OnlyNeedToLoadOneOperand())
                ret.AddRange(associatedCompare.ArgumentTwo.GetILToLoad(context, processor));

            //Will have to be swapped to correct one in post-processing.
            var jumpInstruction = processor.Create(GetJumpOpcode(), dummyTarget);
            ret.Add(jumpInstruction);

            context.RegisterInstructionTargetToSwapOut(jumpInstruction, JumpTarget);

            return ret.ToArray();
        }

        public override bool PseudocodeNeedsLinebreakBefore()
        {
            return true;
        }
    }
}