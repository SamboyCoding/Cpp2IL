using System.Collections.Generic;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

public class TypeAnalysisContext : HasCustomAttributes
{
    public Il2CppTypeDefinition Definition;
    public List<MethodAnalysisContext> Methods = new();
}