using System.Collections.Generic;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public abstract class AnalysisState
    {
        public List<LocalDefinition> FunctionArgumentLocals = new List<LocalDefinition>();
        public Dictionary<string, IAnalysedOperand> RegisterData = new Dictionary<string, IAnalysedOperand>();
        public Dictionary<int, LocalDefinition> StackStoredLocals = new Dictionary<int, LocalDefinition>();
        public Stack<IAnalysedOperand> Stack = new Stack<IAnalysedOperand>();
        public Stack<IAnalysedOperand> FloatingPointStack = new Stack<IAnalysedOperand>();
    }
}