using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Analysis.Actions.Important
{
    public abstract class ConditionalJumpAction : BaseAction
    {
        protected ComparisonAction? associatedCompare;
        protected bool isIfStatement;
        protected bool isIfElse;
        protected bool isWhile;
        protected ulong jumpTarget;
        
        protected ConditionalJumpAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            jumpTarget = instruction.NearBranchTarget;

            if (jumpTarget > instruction.NextIP && jumpTarget < context.AbsoluteMethodEnd)
            {
                isIfStatement = true;
                if(!context.IdentifiedJumpDestinationAddresses.Contains(jumpTarget))
                    context.IdentifiedJumpDestinationAddresses.Add(jumpTarget);
            }

            associatedCompare = (ComparisonAction) context.Actions.LastOrDefault(a => a is ComparisonAction);
            
            if(associatedCompare?.ArgumentOne is LocalDefinition l)
                RegisterUsedLocal(l);
            else if(associatedCompare?.ArgumentOne is ComparisonDirectFieldAccess a)
                RegisterUsedLocal(a.localAccessedOn);
            else if(associatedCompare?.ArgumentOne is ComparisonDirectPropertyAccess p)
                RegisterUsedLocal(p.localAccessedOn);
            
            if(associatedCompare?.ArgumentTwo is LocalDefinition l2)
                RegisterUsedLocal(l2);
            else if(associatedCompare?.ArgumentTwo is ComparisonDirectFieldAccess a2)
                RegisterUsedLocal(a2.localAccessedOn);
            else if(associatedCompare?.ArgumentTwo is ComparisonDirectPropertyAccess p2)
                RegisterUsedLocal(p2.localAccessedOn);

            if (context.IsThereProbablyAnElseAt(jumpTarget))
            {
                //If-Else
                context.RegisterIfElseStatement(instruction.NextIP, jumpTarget, this);
                isIfElse = true;
                context.IndentLevel += 1;
            } else if (associatedCompare?.IsProbablyWhileLoop() == true)
            {
                //While loop
                isWhile = true;
                context.IndentLevel += 1;
            }
            else if(associatedCompare?.unimportantComparison == false)
            {
                //Just an if. No else, no while.
                context.RegisterIfStatement(instruction.NextIP, jumpTarget, this);
                context.IndentLevel += 1;
            }
        }

        protected abstract string GetPseudocodeCondition();

        protected abstract string GetTextSummaryCondition();

        public override bool IsImportant()
        {
            return associatedCompare?.unimportantComparison == false && (isIfElse || isWhile || isIfStatement);
        }

        protected string GetArgumentOnePseudocodeValue()
        {
            return associatedCompare?.ArgumentOne == null ? "" : associatedCompare.ArgumentOne.GetPseudocodeRepresentation();
        }

        protected string GetArgumentTwoPseudocodeValue()
        {
            return associatedCompare?.ArgumentTwo == null ? "" : associatedCompare.ArgumentTwo.GetPseudocodeRepresentation();
        }
        
        public override string? ToPsuedoCode()
        {
            return isWhile ? $"while {GetPseudocodeCondition()}" : $"if {GetPseudocodeCondition()}";
        }

        public override string ToTextSummary()
        {
            return $"Jumps to 0x{jumpTarget:X}{(isIfStatement ? " (which is an if statement's body)" : "")} if {GetTextSummaryCondition()}\n";
        }

        protected virtual bool OnlyNeedToLoadOneOperand() => false;

        protected abstract OpCode GetJumpOpcode();

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions(MethodAnalysis context, ILProcessor processor)
        {
            if (associatedCompare?.ArgumentOne == null || associatedCompare.ArgumentTwo == null)
                throw new TaintedInstructionException();
            
            var ret = new List<Mono.Cecil.Cil.Instruction>();
            var dummyTarget = processor.Create(OpCodes.Nop);
            
            ret.AddRange(associatedCompare.ArgumentOne.GetILToLoad(context, processor));
            
            if(!OnlyNeedToLoadOneOperand())
                ret.AddRange(associatedCompare.ArgumentTwo.GetILToLoad(context, processor));
            
            //Will have to be swapped to correct one in post-processing.
            ret.Add(processor.Create(GetJumpOpcode(), dummyTarget));

            return ret.ToArray();
        }

        public override bool PseudocodeNeedsLinebreakBefore()
        {
            return true;
        }
    }
}