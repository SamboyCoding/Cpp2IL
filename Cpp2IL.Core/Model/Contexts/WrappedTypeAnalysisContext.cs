using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents any type which is just a wrapper with additional info around a base type.
/// For example, pointers, byref types, arrays.
/// </summary>
public class WrappedTypeAnalysisContext : ReferencedTypeAnalysisContext
{
    public WrappedTypeAnalysisContext(Il2CppType rawType, AssemblyAnalysisContext referencedFrom) : base(rawType, referencedFrom)
    {
        ElementType = rawType.Type switch
        {
            Il2CppTypeEnum.IL2CPP_TYPE_PTR => referencedFrom.ResolveIl2CppType(rawType.GetEncapsulatedType()),
            Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY => referencedFrom.ResolveIl2CppType(rawType.GetEncapsulatedType()),
            Il2CppTypeEnum.IL2CPP_TYPE_ARRAY => referencedFrom.ResolveIl2CppType(rawType.GetArrayElementType()),
            Il2CppTypeEnum.IL2CPP_TYPE_BYREF => throw new("TODO Support TYPE_BYREF"),
            _ => throw new($"Type {rawType.Type} is not a wrapper type")
        };
    }
}