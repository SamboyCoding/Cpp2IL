using System.Collections.Generic;
using System.Linq;
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
        protected bool IsImplicitExceptionThrower;
        protected bool IsGoto;
        
        protected AbstractConditionalJumpAction(MethodAnalysis<T> context, ulong branchTarget, T associatedInstruction) : base(context, associatedInstruction)
        {
            JumpTarget = branchTarget;
            
            associatedCompare = (AbstractComparisonAction<T>?) context.Actions.LastOrDefault(a => a is AbstractComparisonAction<T>);
            
            //Check for implicit NRE
            if (IsExceptionThrowWhichIsImplicitInCSharp())
            {
                //Simply an NRE thrower. Important in cpp code, not in managed.
                IsImplicitExceptionThrower = true;
                return;  //Do not increase indent, do not register used local, do not pass go, do not collect $200.
            }
            
            // if (jumpTarget > instruction.NextIP && jumpTarget < context.AbsoluteMethodEnd)
            // {
            //     isIfStatement = true;
            //     if (!context.IdentifiedJumpDestinationAddresses.Contains(jumpTarget))
            //         context.IdentifiedJumpDestinationAddresses.Add(jumpTarget);
            // }
            
            if (associatedCompare?.ArgumentOne is LocalDefinition l)
                RegisterUsedLocal(l, context);
            else if (associatedCompare?.ArgumentOne is ComparisonDirectFieldAccess a)
                RegisterUsedLocal(a.localAccessedOn, context);
            else if (associatedCompare?.ArgumentOne is ComparisonDirectPropertyAccess p)
                RegisterUsedLocal(p.localAccessedOn, context);

            if (associatedCompare?.ArgumentTwo is LocalDefinition l2)
                RegisterUsedLocal(l2, context);
            else if (associatedCompare?.ArgumentTwo is ComparisonDirectFieldAccess a2)
                RegisterUsedLocal(a2.localAccessedOn, context);
            else if (associatedCompare?.ArgumentTwo is ComparisonDirectPropertyAccess p2)
                RegisterUsedLocal(p2.localAccessedOn, context);

            var (currBlockStart, currBlockEnd) = context.GetMostRecentBlock(AssociatedInstruction.GetInstructionAddress());

            if (currBlockEnd != 0 && currBlockEnd < JumpTarget && currBlockStart != associatedCompare?.AssociatedInstruction.GetInstructionAddress())
            {
                //Jumping OUT of the current block - need a goto
                IsGoto = true;
                AddComment($"This is probably a goto, jumping to 0x{JumpTarget:X} which is after end of current block @ 0x{currBlockEnd:X} (started at 0x{currBlockStart:X})");

                if (associatedCompare?.UnimportantComparison == false)
                {
                    context.RegisterGotoDestination(AssociatedInstruction.GetInstructionAddress(), JumpTarget);
                    
                    if(!context.IsJumpDestinationInThisFunction(JumpTarget) && (JumpTarget - context.AbsoluteMethodEnd) < 50)
                        context.ExpandAnalysisToIncludeBlockStartingAt(JumpTarget);
                }

                return;
            }

            if (context.IsThereProbablyAnElseAt(JumpTarget))
            {
                //If-Else
                IsIfElse = true;

                if (associatedCompare?.UnimportantComparison == false)
                {
                    context.RegisterIfElseStatement(associatedInstruction.GetNextInstructionAddress(), JumpTarget, this);
                    AddComment($"Increasing indentation - is if-else, unimportant is {associatedCompare?.UnimportantComparison}");
                    context.IndentLevel += 1;
                }
            }
            else if (associatedCompare?.IsProbablyWhileLoop() == true)
            {
                //While loop
                IsWhile = true;

                if (associatedCompare?.UnimportantComparison == false)
                {
                    AddComment($"Increasing indentation - is while loop, unimportant is {associatedCompare?.UnimportantComparison}");
                    context.IndentLevel += 1;
                }
                
                if(!context.IsJumpDestinationInThisFunction(JumpTarget) && (JumpTarget - context.AbsoluteMethodEnd) < 50)
                    context.ExpandAnalysisToIncludeBlockStartingAt(JumpTarget);
            }
            else if (associatedCompare?.UnimportantComparison == false)
            {
                //Check for array type check
                if (IsArrayTypeCheck(context))
                {
                    AddComment("Skipping if statement, is array type check");
                    IsImplicitExceptionThrower = true;
                    return;
                }

                //Just an if. No else, no while.
                context.RegisterIfStatement(associatedInstruction.GetNextInstructionAddress(), JumpTarget, this);

                IsIfStatement = true;
                AddComment($"Increasing indentation - is standard if, unimportant is {associatedCompare?.UnimportantComparison}");
                context.IndentLevel += 1;
                
                if(!context.IsJumpDestinationInThisFunction(JumpTarget) && (JumpTarget - context.AbsoluteMethodEnd) < 50)
                    context.ExpandAnalysisToIncludeBlockStartingAt(JumpTarget);
            }
        }
        
        protected abstract string GetPseudocodeCondition();

        protected abstract string GetInvertedPseudocodeConditionForGotos();

        protected abstract string GetTextSummaryCondition();
        
        protected virtual bool OnlyNeedToLoadOneOperand() => false;

        protected virtual bool IsArrayTypeCheck(MethodAnalysis<T> methodAnalysis) => false;

        protected abstract bool IsExceptionThrowWhichIsImplicitInCSharp();
        protected abstract OpCode GetJumpOpcode();

        public sealed override bool IsImportant()
        {
            return !IsImplicitExceptionThrower && associatedCompare?.UnimportantComparison == false && (IsIfElse || IsWhile || IsIfStatement || IsGoto);
        }

        protected string GetArgumentOnePseudocodeValue()
        {
            return associatedCompare?.ArgumentOne == null ? "" : associatedCompare.ArgumentOne.GetPseudocodeRepresentation();
        }

        protected string GetArgumentTwoPseudocodeValue()
        {
            return associatedCompare?.ArgumentTwo == null ? "" : associatedCompare.ArgumentTwo.GetPseudocodeRepresentation();
        }

        public sealed override string ToPsuedoCode()
        {
            if (IsGoto)
            {
                return $"if {GetInvertedPseudocodeConditionForGotos()}\n" +
                       $"    goto INSN_{JumpTarget:X}\n" +
                       $"endif";
            }
            
            return IsWhile ? $"while {GetPseudocodeCondition()}" : $"if {GetPseudocodeCondition()}";
        }

        public sealed override string ToTextSummary()
        {
            if (IsImplicitExceptionThrower)
                return $"Jumps to 0x{JumpTarget:X} (which throws a NRE) if {GetTextSummaryCondition()}. Implicitly present in managed code, so ignored here.";

            return $"Jumps to 0x{JumpTarget:X}{(IsIfStatement ? " (which is an if statement's body)" : "")} if {GetTextSummaryCondition()}\n";
        }


        public sealed override Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor)
        {
            if (associatedCompare?.ArgumentOne == null || (!OnlyNeedToLoadOneOperand() && associatedCompare.ArgumentTwo == null))
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

        public sealed override bool PseudocodeNeedsLinebreakBefore()
        {
            return true;
        }
    }
}