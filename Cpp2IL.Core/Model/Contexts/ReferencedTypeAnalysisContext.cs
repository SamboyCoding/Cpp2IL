using System;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents any kind of type context that is not a basic type definition. This includes generic instantiations, byref/pointer types, arrays, etc.
/// </summary>
public abstract class ReferencedTypeAnalysisContext(AssemblyAnalysisContext referencedFrom)
    : TypeAnalysisContext(null, referencedFrom)
{
    public override Il2CppTypeEnum Type => throw new NotImplementedException("Type must be set by derived classes");

    protected override int CustomAttributeIndex => -1;

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringAssembly;

    public override string ToString()
    {
        return DefaultName;
    }

    public override string GetCSharpSourceString()
    {
        return Name;
    }
}
