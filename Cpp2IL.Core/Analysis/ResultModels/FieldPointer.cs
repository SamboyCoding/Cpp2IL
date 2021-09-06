namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class FieldPointer<T>
    {
        public FieldUtils.FieldBeingAccessedData Field;
        public LocalDefinition<T> OnWhat;

        public FieldPointer(FieldUtils.FieldBeingAccessedData field, LocalDefinition<T> onWhat)
        {
            Field = field;
            OnWhat = onWhat;
        }
    }
}