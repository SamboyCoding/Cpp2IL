using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class InjectedParameterAnalysisContext : ParameterAnalysisContext
{
    public override TypeAnalysisContext ParameterTypeContext { get; }

    public override bool IsRef => false; //For now

    public InjectedParameterAnalysisContext(string? name, Il2CppType type, int paramIndex, MethodAnalysisContext declaringMethod)
        : this(name, declaringMethod.DeclaringType!.DeclaringAssembly.ResolveIl2CppType(type) ?? throw new($"Type {type} could not be resolved."), paramIndex, declaringMethod)
    {
    }

    public InjectedParameterAnalysisContext(string? name, TypeAnalysisContext typeContext, int paramIndex, MethodAnalysisContext declaringMethod) : base(null, paramIndex, declaringMethod)
    {
        OverrideName = name ?? $"param_{paramIndex}";
        ParameterTypeContext = typeContext;
    }
}
