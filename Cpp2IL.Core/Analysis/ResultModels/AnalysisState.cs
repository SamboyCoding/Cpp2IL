using System.Collections.Generic;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public abstract class AnalysisState
    {
        public List<LocalDefinition> FunctionArgumentLocals = new();
        public Dictionary<string, IAnalysedOperand> RegisterData = new();
        public Dictionary<int, LocalDefinition> StackStoredLocals = new();
        public Stack<IAnalysedOperand> Stack = new();
        public Stack<IAnalysedOperand> FloatingPointStack = new();
    }
}