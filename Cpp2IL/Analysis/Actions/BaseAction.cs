using System.Text;
using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;

namespace Cpp2IL.Analysis.Actions
{
    public abstract class BaseAction
    {
        private StringBuilder _lineComments = new StringBuilder();
        public Instruction AssociatedInstruction;
        public int IndentLevel;
        
        public BaseAction(MethodAnalysis context, Instruction instruction)
        {
            AssociatedInstruction = instruction;
            IndentLevel = context.IndentLevel;
        }

        public abstract Mono.Cecil.Cil.Instruction[] ToILInstructions();

        public abstract string? ToPsuedoCode();

        public abstract string ToTextSummary();

        public virtual bool IsImportant()
        {
            return false;
        }

        public string GetSynopsisEntry()
        {
            var comment = GetLineComment();

            if (string.IsNullOrWhiteSpace(comment))
                return ToTextSummary();

            var summary = ToTextSummary();
            
            var newlineCount = 0;
            if (summary.EndsWith("\n"))
            {
                var oldLen = summary.Length;
                summary = summary.TrimEnd('\n');
                newlineCount = summary.Length - oldLen;
            }

            return $"{summary} ; {GetLineComment()}{"\n".Repeat(newlineCount)}";
        }

        protected void AddComment(string comment)
        {
            _lineComments.Append(" - ").Append(comment);
        }

        public string GetLineComment()
        {
            return _lineComments.ToString();
        }

        public virtual bool PseudocodeNeedsLinebreakBefore()
        {
            return false;
        }
    }
}