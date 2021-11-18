namespace Cpp2IL.Core.Analysis.ResultModels
{
    public class LocalPointer
    {
        public readonly LocalDefinition Local;
        public LocalPointer(LocalDefinition localDefinition)
        {
            Local = localDefinition;
        }
    }
}