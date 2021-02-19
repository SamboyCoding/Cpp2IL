using Cpp2IL.Analysis.Actions;

namespace Cpp2IL.Analysis.ResultModels
{
    public class IfData : AnalysisState
    {
        public ulong IfStatementStart;
        public ulong IfStatementEnd;
        public BaseAction ConditionalJumpStatement;
    }
}