using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

public class MethodAnalysisContext : HasCustomAttributes
{
    public Il2CppMethodDefinition Definition;
    
    public byte[] RawBytes;

    public void Analyze()
    {
        
    }
}