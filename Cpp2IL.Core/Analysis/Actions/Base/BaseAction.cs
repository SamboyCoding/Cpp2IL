using System.Collections.Generic;
using System.Text;
using Cpp2IL.Core.Analysis.ResultModels;
using LibCpp2IL;
using Mono.Cecil.Cil;

namespace Cpp2IL.Core.Analysis.Actions.Base
{
    public abstract class BaseAction<T>
    {
        public T AssociatedInstruction;
        private StringBuilder _lineComments = new StringBuilder();
        
        public int IndentLevel;

        private List<LocalDefinition<T>> UsedLocals = new();
        private List<LocalDefinition<T>> RegisteredLocalsWithoutSideEffects = new();

        protected bool is32Bit => LibCpp2IlMain.Binary!.is32Bit;
        
        public BaseAction(MethodAnalysis<T> context, T associatedInstruction)
        {
            IndentLevel = context.IndentLevel;
            AssociatedInstruction = associatedInstruction;
        }

        public abstract Instruction[] ToILInstructions(MethodAnalysis<T> context, ILProcessor processor);

        public abstract string? ToPsuedoCode();

        public abstract string ToTextSummary();

        public List<LocalDefinition<T>> GetUsedLocals()
        {
            return UsedLocals;
        }

        protected void RegisterUsedLocal(LocalDefinition<T> l)
        {
            UsedLocals.Add(l);
        }
        
        public List<LocalDefinition<T>> GetRegisteredLocalsWithoutSideEffects()
        {
            return RegisteredLocalsWithoutSideEffects;
        }

        protected void RegisterDefinedLocalWithoutSideEffects(LocalDefinition<T> l)
        {
            RegisteredLocalsWithoutSideEffects.Add(l);
        }

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
                newlineCount = oldLen - summary.Length;
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