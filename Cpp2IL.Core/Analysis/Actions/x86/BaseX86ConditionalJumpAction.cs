using System.Linq;
using Cpp2IL.Core.Analysis.Actions.Base;
using Cpp2IL.Core.Analysis.Actions.x86.Important;
using Cpp2IL.Core.Analysis.ResultModels;
using Iced.Intel;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Analysis.Actions.x86
{
    public abstract class BaseX86ConditionalJumpAction : AbstractConditionalJumpAction<Instruction>
    {
        protected BaseX86ConditionalJumpAction(MethodAnalysis<Instruction> context, Instruction instruction) : base(context, instruction)
        {
            JumpTarget = instruction.NearBranchTarget;

            // if (jumpTarget > instruction.NextIP && jumpTarget < context.AbsoluteMethodEnd)
            // {
            //     isIfStatement = true;
            //     if (!context.IdentifiedJumpDestinationAddresses.Contains(jumpTarget))
            //         context.IdentifiedJumpDestinationAddresses.Add(jumpTarget);
            // }

            associatedCompare = (ComparisonAction?) context.Actions.LastOrDefault(a => a is ComparisonAction);

            //Check for implicit NRE
            var body = Utils.GetMethodBodyAtVirtAddressNew(JumpTarget, true);

            if (body.Count > 0 && body[0].Mnemonic == Mnemonic.Call && CallExceptionThrowerFunction.IsExceptionThrower(body[0].NearBranchTarget))
            {
                if (CallExceptionThrowerFunction.GetExceptionThrown(body[0].NearBranchTarget)?.Name == "NullReferenceException")
                {
                    //Simply an NRE thrower. Important in cpp code, not in managed.
                    IsImplicitNullReferenceException = true;
                    return; //Do not increase indent, do not register used local, do not pass go, do not collect $200.
                }
            }

            //TODO try and move this lot into the parent class.
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

            var (currBlockStart, currBlockEnd) = context.GetMostRecentBlock(AssociatedInstruction.IP);

            if (currBlockEnd != 0 && currBlockEnd < JumpTarget && currBlockStart != associatedCompare?.AssociatedInstruction.IP)
            {
                //Jumping OUT of the current block - need a goto
                IsGoto = true;
                AddComment($"This is probably a goto, jumping to 0x{JumpTarget:X} which is after end of current block @ 0x{currBlockEnd:X} (started at 0x{currBlockStart:X})");

                if (associatedCompare?.UnimportantComparison == false)
                {
                    context.RegisterGotoDestination(AssociatedInstruction.IP, JumpTarget);
                    
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
                    context.RegisterIfElseStatement(instruction.NextIP, JumpTarget, this);
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
                //Just an if. No else, no while.
                context.RegisterIfStatement(instruction.NextIP, JumpTarget, this);

                IsIfStatement = true;
                AddComment($"Increasing indentation - is standard if, unimportant is {associatedCompare?.UnimportantComparison}");
                context.IndentLevel += 1;
                
                if(!context.IsJumpDestinationInThisFunction(JumpTarget) && (JumpTarget - context.AbsoluteMethodEnd) < 50)
                    context.ExpandAnalysisToIncludeBlockStartingAt(JumpTarget);
            }
        }

        
    }
}