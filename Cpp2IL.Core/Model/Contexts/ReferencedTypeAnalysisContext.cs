using System.Collections.Generic;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents any kind of type context that is not a basic type definition. This includes generic instantiations, byref/pointer types, arrays, etc.
/// </summary>
public abstract class ReferencedTypeAnalysisContext : TypeAnalysisContext
{
    public abstract Il2CppTypeEnum Type { get; } //Must be set by derived classes

    protected abstract TypeAnalysisContext ElementType { get; } //Must be set by derived classes

    protected List<TypeAnalysisContext> GenericArguments { get; } = new();

    public override string DefaultNs => ElementType.Namespace;

    protected override int CustomAttributeIndex => -1;

    public sealed override bool IsGenericInstance => GenericArguments.Count > 0;

    public sealed override int GenericParameterCount => GenericArguments.Count;

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringAssembly;

    protected ReferencedTypeAnalysisContext(AssemblyAnalysisContext referencedFrom) : base(null, referencedFrom)
    {
    }

    public override string ToString()
    {
        return DefaultName;
    }

    public override string GetCSharpSourceString()
    {
        return Name;
    }
}
