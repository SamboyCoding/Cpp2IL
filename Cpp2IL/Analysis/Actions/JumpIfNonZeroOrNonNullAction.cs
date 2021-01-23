using System.Linq;
using Cpp2IL.Analysis.ResultModels;
using LibCpp2IL;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public class JumpIfNonZeroOrNonNullAction : BaseAction
    {
        private ComparisonAction? associatedCompare;
        private bool nullMode;
        private bool booleanMode;
        private ulong jumpTarget;
        private bool isIfStatement;
        private bool isIfElse;
        
        public JumpIfNonZeroOrNonNullAction(MethodAnalysis context, Instruction instruction) : base(context, instruction)
        {
            jumpTarget = instruction.NearBranchTarget;

            if (jumpTarget > instruction.NextIP && jumpTarget < context.AbsoluteMethodEnd)
            {
                isIfStatement = true;
                if(!context.IdentifiedJumpDestinationAddresses.Contains(jumpTarget))
                    context.IdentifiedJumpDestinationAddresses.Add(jumpTarget);
            }

            associatedCompare = (ComparisonAction) context.Actions.LastOrDefault(a => a is ComparisonAction);
            if (associatedCompare != null)
            {
                nullMode = associatedCompare.ArgumentOne == associatedCompare.ArgumentTwo;
                booleanMode = nullMode && associatedCompare.ArgumentOne is LocalDefinition local && local.Type?.FullName == "System.Boolean";
            }

            if (context.IsThereProbablyAnElseAt(jumpTarget))
            {
                context.RegisterIfElseStatement(instruction.NextIP, jumpTarget, this);
                isIfElse = true;
                context.IndentLevel += 1;
            }

        }

        public override Mono.Cecil.Cil.Instruction[] ToILInstructions()
        {
            throw new System.NotImplementedException();
        }

        private string GetArgumentOnePseudocodeValue()
        {
            if (associatedCompare?.ArgumentOne == null) return "";
            
            var operand = associatedCompare!.ArgumentOne;
            if (operand is LocalDefinition localDefinition)
                return localDefinition.Name;

            var constant = (ConstantDefinition) operand;
            var stringRep = constant.ToString();
            if (stringRep.StartsWith("{"))
                return constant.Name;

            return stringRep;
        }
        
        private string GetArgumentTwoPseudocodeValue()
        {
            if (associatedCompare?.ArgumentTwo == null) return "";
            
            var operand = associatedCompare!.ArgumentTwo;
            if (operand is LocalDefinition localDefinition)
                return localDefinition.Name;

            var constant = (ConstantDefinition) operand;
            var stringRep = constant.ToString();
            if (stringRep.StartsWith("{"))
                return constant.Name;

            return stringRep;
        }

        public override string? ToPsuedoCode()
        {
            //We have to invert the condition, so in this case we want "is false", "is null", or "is equal"
            if (booleanMode)
                return $"if (!{GetArgumentOnePseudocodeValue()})";

            if (nullMode)
                return $"if ({GetArgumentOnePseudocodeValue()} == null)";

            if (associatedCompare != null)
                return $"if {GetArgumentOnePseudocodeValue()} == {GetArgumentTwoPseudocodeValue()})";

            return "if (<missing compare>)";
        }

        public override string ToTextSummary()
        {
            if(booleanMode)
                return $"Jumps to 0x{jumpTarget:X}{(isIfStatement ? " (which is an if statement's body)" : "")} if {associatedCompare!.ArgumentOne} is true\n";
            
            if (nullMode)
                return $"Jumps to 0x{jumpTarget:X}{(isIfStatement ? " (which is an if statement's body)" : "")} if {associatedCompare!.ArgumentOne} is not null\n";
            
            if(associatedCompare != null)
                return $"Jumps to 0x{jumpTarget:X}{(isIfStatement ? " (which is an if statement's body)" : "")} if {associatedCompare.ArgumentOne} != {associatedCompare.ArgumentTwo}\n";
            
            return $"Jumps to 0x{jumpTarget:X}{(isIfStatement ? " (which is an if statement's body)" : "")} if the compare showed the two items were not equal\n";
        }

        public override bool IsImportant()
        {
            return isIfElse;
        }
    }
}