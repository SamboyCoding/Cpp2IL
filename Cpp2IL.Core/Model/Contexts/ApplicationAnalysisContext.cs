using System.Collections.Generic;
using LibCpp2IL;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Top-level class to represent an individual il2cpp application that has been loaded into cpp2il.
/// </summary>
public class ApplicationAnalysisContext
{
    public Il2CppBinary Binary;
    public Il2CppMetadata Metadata;
    
    public List<AssemblyAnalysisContext> Assemblies = new();
}