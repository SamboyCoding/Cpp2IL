using System.Collections.Generic;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public abstract class AnalysisState<T>
    {
        public List<LocalDefinition<T>> FunctionArgumentLocals = new();
        public Dictionary<string, IAnalysedOperand<T>> RegisterData = new();
        public Dictionary<int, LocalDefinition<T>> StackStoredLocals = new();
        public Stack<IAnalysedOperand<T>> Stack = new();
        public Stack<IAnalysedOperand<T>> FloatingPointStack = new();
    }
}