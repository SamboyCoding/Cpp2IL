using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using Mono.Cecil.Cil;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.Important
{
    public abstract class ConditionalJumpAction : BaseAction
    {
        protected ComparisonAction? associatedCompare;
        protected bool isIfStatement;
        protected bool isIfElse;
        protected bool isWhile;
        protected bool isImplicitNullReferenceException;
        protected ulong jumpTarget;

        protected ConditionalJumpAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            jumpTarget = instruction.NearBranchTarget;

            if (jumpTarget > instruction.NextIP && jumpTarget < context.AbsoluteMethodEnd)
            {
                isIfStatement = true;
                if (!context.IdentifiedJumpDestinationAddresses.Contains(jumpTarget))
                    context.IdentifiedJumpDestinationAddresses.Add(jumpTarget);
            }

            associatedCompare = (ComparisonAction?) context.Actions.LastOrDefault(a => a is ComparisonAction);
            
            //Check for implicit NRE
            var body = Utils.GetMethodBodyAtVirtAddressNew(jumpTarget, true);

            if (body.Count > 0 && body[0].Mnemonic == Mnemonic.Call && CallExceptionThrowerFunction.IsExceptionThrower(body[0].NearBranchTarget))
            {
                if (CallExceptionThrowerFunction.GetExceptionThrown(body[0].NearBranchTarget)?.Name == "NullReferenceException")
                {
                    //Simply an NRE thrower. Important in cpp code, not in managed.
                    isImplicitNullReferenceException = true;
                    return; //Do not increase indent, do not register used local, do not pass go, do not collect $200.
                }
            }

            if (associatedCompare?.ArgumentOne is LocalDefinition l)
                RegisterUsedLocal(l);
            else if (associatedCompare?.ArgumentOne is ComparisonDirectFieldAccess a)
                RegisterUsedLocal(a.localAccessedOn);
            else if (associatedCompare?.ArgumentOne is ComparisonDirectPropertyAccess p)
                RegisterUsedLocal(p.localAccessedOn);

            if (associatedCompare?.ArgumentTwo is LocalDefinition l2)
                RegisterUsedLocal(l2);
            else if (associatedCompare?.ArgumentTwo is ComparisonDirectFieldAccess a2)
                RegisterUsedLocal(a2.localAccessedOn);
            else if (associatedCompare?.ArgumentTwo is ComparisonDirectPropertyAccess p2)
                RegisterUsedLocal(p2.localAccessedOn);

            if (context.IsThereProbablyAnElseAt(jumpTarget))
            {
                //If-Else
                context.RegisterIfElseStatement(instruction.NextIP, jumpTarget, this);
                isIfElse = true;

                if (associatedCompare?.unimportantComparison == false)
                {
                    AddComment($"Increasing indentation - is if-else, unimportant is {associatedCompare?.unimportantComparison}");
                    context.IndentLevel += 1;
                }
            }
            else if (associatedCompare?.IsProbablyWhileLoop() == true)
            {
                //While loop
                isWhile = true;

                if (associatedCompare?.unimportantComparison == false)
                {
                    AddComment($"Increasing indentation - is while loop, unimportant is {associatedCompare?.unimportantComparison}");
                    context.IndentLevel += 1;
                }
            }
            else if (associatedCompare?.unimportantComparison == false)
            {
                //Just an if. No else, no while.
                context.RegisterIfStatement(instruction.NextIP, jumpTarget, this);

                isIfStatement = true;

                if (associatedCompare?.unimportantComparison == false)
                {
                    AddComment($"Increasing indentation - is standard if, unimportant is {associatedCompare?.unimportantComparison}");
                    context.IndentLevel += 1;
                }
            }
        }

        protected abstract string GetPseudocodeCondition();

        protected abstract string GetTextSummaryCondition();

        public override bool IsImportant()
        {
            return !isImplicitNullReferenceException && associatedCompare?.unimportantComparison == false && (isIfElse || isWhile || isIfStatement);
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
            if (isImplicitNullReferenceException)
                return $"Jumps to 0x{jumpTarget:X} (which throws a NRE) if {GetTextSummaryCondition()}. Implicitly present in managed code, so ignored here.";

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

            if (!OnlyNeedToLoadOneOperand())
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