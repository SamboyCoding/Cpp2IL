using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents any type which is just a wrapper with additional info around a base type.
/// For example, pointers, byref types, arrays.
/// </summary>
public abstract class WrappedTypeAnalysisContext : ReferencedTypeAnalysisContext
{
    protected override TypeAnalysisContext ElementType { get; }

    protected WrappedTypeAnalysisContext(TypeAnalysisContext elementType, AssemblyAnalysisContext referencedFrom) : base(referencedFrom)
    {
        ElementType = elementType;
    }

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
