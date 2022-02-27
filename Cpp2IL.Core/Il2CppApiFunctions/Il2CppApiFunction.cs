namespace Cpp2IL.Core.Il2CppApiFunctions;

public class Il2CppApiFunction
{
    public string Name;
    public ParameterType[] ParametersTypes;
    public ParameterType ReturnType;

    public enum ApiFunctionType
    {
        Exported,
        WrapperOf,
        WrappedBy
    }

    public enum ParameterType
    {
        Standard,
        Float
    }
}