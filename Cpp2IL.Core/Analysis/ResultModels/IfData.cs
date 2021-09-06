using Cpp2IL.Core.Analysis.Actions.Base;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class IfData<T> : AnalysisState<T>
    {
        public ulong IfStatementStart;
        public ulong IfStatementEnd;
        public BaseAction<T> ConditionalJumpStatement;
    }
}