using System.Collections.Generic;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

public class AssemblyAnalysisContext : HasCustomAttributes
{
    public Il2CppAssemblyDefinition Definition;
    public List<TypeAnalysisContext> Types = new();
}