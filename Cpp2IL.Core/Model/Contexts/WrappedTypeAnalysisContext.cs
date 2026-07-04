using System;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents any type which is just a wrapper with additional info around an element type.
/// For example, pointers, byref types, arrays.
/// </summary>
public abstract class WrappedTypeAnalysisContext(
    TypeAnalysisContext elementType) : ReferencedTypeAnalysisContext(elementType.DeclaringAssembly)
{
    public virtual TypeAnalysisContext ElementType { get; } = elementType;

    public sealed override string DefaultNamespace => ElementType.DefaultNamespace;

    public sealed override string? OverrideNamespace
    {
        get => ElementType.OverrideNamespace;
        set => ElementType.OverrideNamespace = value;
    }

    public static WrappedTypeAnalysisContext Create(Il2CppType rawType, ApplicationAnalysisContext context)
    {
        return rawType.Type switch
        {
            Il2CppTypeEnum.IL2CPP_TYPE_PTR => new PointerTypeAnalysisContext(rawType, context),
            Il2CppTypeEnum.IL2CPP_TYPE_BYREF => throw new NotSupportedException("ByRef types have a dedicated flag"),
            Il2CppTypeEnum.IL2CPP_TYPE_ARRAY => new ArrayTypeAnalysisContext(rawType, context),
            Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY => new SzArrayTypeAnalysisContext(rawType, context),
            Il2CppTypeEnum.IL2CPP_TYPE_BOXED => new BoxedTypeAnalysisContext(rawType, context),
            Il2CppTypeEnum.IL2CPP_TYPE_PINNED => new PinnedTypeAnalysisContext(rawType, context),
            _ => throw new($"Type {rawType.Type} is not a wrapper type")
        };
    }
}
