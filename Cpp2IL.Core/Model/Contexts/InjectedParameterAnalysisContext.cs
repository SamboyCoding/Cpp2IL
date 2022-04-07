using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedParameterAnalysisContext : ParameterAnalysisContext
{
    public override Il2CppType ParameterType { get; }

    public override bool IsRef => false; //For now

    public InjectedParameterAnalysisContext(string? name, Il2CppType type, MethodAnalysisContext declaringMethod) : base(null, 0, declaringMethod)
    {
        OverrideName = name;
        ParameterType = type;
    }

    public InjectedParameterAnalysisContext(string? name, TypeAnalysisContext typeContext, MethodAnalysisContext declaringMethod)
        : this(name, LibCpp2IlReflection.GetTypeFromDefinition(typeContext.Definition ?? throw new("Parameters with a type of an injected type are not supported yet."))!, declaringMethod)
    {
        
    }
}