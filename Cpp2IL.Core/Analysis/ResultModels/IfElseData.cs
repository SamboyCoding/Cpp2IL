using Cpp2IL.Core.Analysis.Actions.Base;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class IfElseData<T> : AnalysisState
    {
        public ulong IfStatementStart;
        public ulong ElseStatementStart;
        public ulong ElseStatementEnd;
        public BaseAction<T> ConditionalJumpStatement;
    }
}