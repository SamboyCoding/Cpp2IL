using System.Collections.Generic;
using Cpp2IL.Analysis.Actions;

namespace Cpp2IL.Analysis.ResultModels
{
    public class IfElseData
    {
        public List<LocalDefinition> FunctionArgumentLocals = new List<LocalDefinition>();
        public Dictionary<string, IAnalysedOperand> RegisterData = new Dictionary<string, IAnalysedOperand>();
        public Dictionary<int, LocalDefinition> StackStoredLocals = new Dictionary<int, LocalDefinition>();
        public Stack<IAnalysedOperand> Stack = new Stack<IAnalysedOperand>();

        public ulong IfStatementStart;
        public ulong ElseStatementStart;
        public ulong ElseStatementEnd;
        public BaseAction ConditionalJumpStatement;
    }
}