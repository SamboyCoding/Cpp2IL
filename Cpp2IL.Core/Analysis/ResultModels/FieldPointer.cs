namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class FieldPointer
    {
        public FieldUtils.FieldBeingAccessedData Field;
        public LocalDefinition OnWhat;

        public FieldPointer(FieldUtils.FieldBeingAccessedData field, LocalDefinition onWhat)
        {
            Field = field;
            OnWhat = onWhat;
        }
    }
}