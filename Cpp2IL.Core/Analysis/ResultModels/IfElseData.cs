using Cpp2IL.Core.Analysis.Actions;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class IfElseData : AnalysisState
    {
        public ulong IfStatementStart;
        public ulong ElseStatementStart;
        public ulong ElseStatementEnd;
        public BaseAction ConditionalJumpStatement;
    }
}