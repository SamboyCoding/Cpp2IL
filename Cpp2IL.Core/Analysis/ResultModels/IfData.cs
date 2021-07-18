using Cpp2IL.Core.Analysis.Actions;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class IfData : AnalysisState
    {
        public ulong IfStatementStart;
        public ulong IfStatementEnd;
        public BaseAction ConditionalJumpStatement;
    }
}