namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class IntegerDivisionInProgress<T>
    {
        public IAnalysedOperand<T> OriginalValue;
        public ulong MultipliedBy;
        public int ShiftCount;
        public bool TookTopHalf;
        public bool IsComplete;

        public IntegerDivisionInProgress(IAnalysedOperand<T> originalValue, ulong multipliedBy)
        {
            OriginalValue = originalValue;
            MultipliedBy = multipliedBy;
        }
    }
}