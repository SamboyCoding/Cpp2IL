using System.Text;
using Cpp2IL.Analysis.ResultModels;
using SharpDisasm;

namespace Cpp2IL.Analysis.Actions
{
    public abstract class BaseAction
    {
        private StringBuilder _lineComments = new StringBuilder();
        
        public BaseAction(MethodAnalysis context, Instruction instruction)
        {
            
        }

        public abstract Mono.Cecil.Cil.Instruction[] ToILInstructions();

        public abstract string? ToPsuedoCode();

        public abstract string ToTextSummary();

        protected void AddComment(string comment)
        {
            _lineComments.Append(" - ").Append(comment);
        }

        public string GetLineComment()
        {
            return _lineComments.ToString();
        }
    }
}