using Cpp2IL.Core.Analysis.Actions.Base;

namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class IfData : AnalysisState
    {
        public ulong IfStatementStart;
        public ulong IfStatementEnd;
        public BaseAction<Iced.Intel.Instruction> ConditionalJumpStatement;
    }
}