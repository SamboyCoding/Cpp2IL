namespace Cpp2IL.Core.Model.Contexts;

public class SystemTypesContext
{
    private ApplicationAnalysisContext _appContext;

    public TypeAnalysisContext SystemObjectType { get; }
    public TypeAnalysisContext SystemStringType { get; }
    public TypeAnalysisContext SystemInt32Type { get; }
    public TypeAnalysisContext SystemInt64Type { get; }
    public TypeAnalysisContext SystemBooleanType { get; }
    public TypeAnalysisContext SystemVoidType { get; }
    public TypeAnalysisContext SystemExceptionType { get; }

    public SystemTypesContext(ApplicationAnalysisContext appContext)
    {
        _appContext = appContext;

        var systemAssembly = _appContext.GetAssemblyByName("mscorlib") ?? throw new("Could not find system assembly");
        
        SystemObjectType = systemAssembly.GetTypeByFullName("System.Object")!;
        SystemStringType = systemAssembly.GetTypeByFullName("System.String")!;
        SystemInt32Type = systemAssembly.GetTypeByFullName("System.Int32")!;
        SystemInt64Type = systemAssembly.GetTypeByFullName("System.Int64")!;
        SystemBooleanType = systemAssembly.GetTypeByFullName("System.Boolean")!;
        SystemVoidType = systemAssembly.GetTypeByFullName("System.Void")!;
        SystemExceptionType = systemAssembly.GetTypeByFullName("System.Exception")!;
    }
}