using Cpp2IL.Analysis.Actions;

namespace Cpp2IL.Analysis.ResultModels
{
    public class IfElseData : AnalysisState
    {
        public ulong IfStatementStart;
        public ulong ElseStatementStart;
        public ulong ElseStatementEnd;
        public BaseAction ConditionalJumpStatement;
    }
}