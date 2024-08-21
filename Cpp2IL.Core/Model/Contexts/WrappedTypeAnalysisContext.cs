using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents any type which is just a wrapper with additional info around an element type.
/// For example, pointers, byref types, arrays.
/// </summary>
public abstract class WrappedTypeAnalysisContext(
    TypeAnalysisContext elementType,
    AssemblyAnalysisContext referencedFrom) : ReferencedTypeAnalysisContext(referencedFrom)
{
    public virtual TypeAnalysisContext ElementType { get; } = elementType;

    public override string DefaultNs => ElementType.Namespace;

    public static WrappedTypeAnalysisContext Create(Il2CppType rawType, AssemblyAnalysisContext referencedFrom)
    {
        return rawType.Type switch
        {
            Il2CppTypeEnum.IL2CPP_TYPE_PTR => new PointerTypeAnalysisContext(rawType, referencedFrom),
            Il2CppTypeEnum.IL2CPP_TYPE_BYREF => new ByRefTypeAnalysisContext(rawType, referencedFrom),
            Il2CppTypeEnum.IL2CPP_TYPE_ARRAY => new ArrayTypeAnalysisContext(rawType, referencedFrom),
            Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY => new SzArrayTypeAnalysisContext(rawType, referencedFrom),
            _ => throw new($"Type {rawType.Type} is not a wrapper type")
        };
    }
}
