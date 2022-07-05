namespace Cpp2IL.Core.Model.Contexts;

public class SystemTypesContext
{
    private ApplicationAnalysisContext _appContext;

    public TypeAnalysisContext SystemObjectType { get; }
    public TypeAnalysisContext SystemVoidType { get; }
    public TypeAnalysisContext SystemBooleanType { get; }
    public TypeAnalysisContext SystemCharType { get; }
    public TypeAnalysisContext SystemSByteType { get; }
    public TypeAnalysisContext SystemByteType { get; }
    public TypeAnalysisContext SystemInt16Type { get; }
    public TypeAnalysisContext SystemUInt16Type { get; }
    public TypeAnalysisContext SystemInt32Type { get; }
    public TypeAnalysisContext SystemUInt32Type { get; }
    public TypeAnalysisContext SystemInt64Type { get; }
    public TypeAnalysisContext SystemUInt64Type { get; }
    public TypeAnalysisContext SystemSingleType { get; }
    public TypeAnalysisContext SystemDoubleType { get; }
    public TypeAnalysisContext SystemIntPtrType { get; }
    public TypeAnalysisContext SystemUIntPtrType { get; }
    public TypeAnalysisContext SystemExceptionType { get; }
    public TypeAnalysisContext SystemStringType { get; }
    public TypeAnalysisContext SystemTypedReferenceType { get; }
    public TypeAnalysisContext SystemTypeType { get; }
    public TypeAnalysisContext SystemAttributeType { get; }

    public SystemTypesContext(ApplicationAnalysisContext appContext)
    {
        _appContext = appContext;

        var systemAssembly = _appContext.GetAssemblyByName("mscorlib") ?? throw new("Could not find system assembly");
        
        SystemObjectType = systemAssembly.GetTypeByFullName("System.Object")!;
        SystemVoidType = systemAssembly.GetTypeByFullName("System.Void")!;
        
        SystemBooleanType = systemAssembly.GetTypeByFullName("System.Boolean")!;
        SystemCharType = systemAssembly.GetTypeByFullName("System.Char")!;
        
        SystemSByteType = systemAssembly.GetTypeByFullName("System.SByte")!;
        SystemByteType = systemAssembly.GetTypeByFullName("System.Byte")!;
        
        SystemInt16Type = systemAssembly.GetTypeByFullName("System.Int16")!;
        SystemUInt16Type = systemAssembly.GetTypeByFullName("System.UInt16")!;

        SystemInt32Type = systemAssembly.GetTypeByFullName("System.Int32")!;
        SystemUInt32Type = systemAssembly.GetTypeByFullName("System.UInt32")!;
        
        SystemInt64Type = systemAssembly.GetTypeByFullName("System.Int64")!;
        SystemUInt64Type = systemAssembly.GetTypeByFullName("System.UInt64")!;
        
        SystemSingleType = systemAssembly.GetTypeByFullName("System.Single")!;
        SystemDoubleType = systemAssembly.GetTypeByFullName("System.Double")!;
        
        SystemIntPtrType = systemAssembly.GetTypeByFullName("System.IntPtr")!;
        SystemUIntPtrType = systemAssembly.GetTypeByFullName("System.UIntPtr")!;

        SystemStringType = systemAssembly.GetTypeByFullName("System.String")!;
        SystemTypedReferenceType = systemAssembly.GetTypeByFullName("System.TypedReference")!;
        SystemTypeType = systemAssembly.GetTypeByFullName("System.Type")!;
        
        SystemExceptionType = systemAssembly.GetTypeByFullName("System.Exception")!;
        SystemAttributeType = systemAssembly.GetTypeByFullName("System.Attribute")!;
    }
}