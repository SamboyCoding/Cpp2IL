using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents any type which is just a wrapper with additional info around a base type.
/// For example, pointers, byref types, arrays.
/// </summary>
public class WrappedTypeAnalysisContext : ReferencedTypeAnalysisContext
{
    protected override TypeAnalysisContext ElementType => RawType.Type switch
    {
        Il2CppTypeEnum.IL2CPP_TYPE_PTR => DeclaringAssembly.ResolveIl2CppType(RawType.GetEncapsulatedType()),
        Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY => DeclaringAssembly.ResolveIl2CppType(RawType.GetEncapsulatedType()),
        Il2CppTypeEnum.IL2CPP_TYPE_ARRAY => DeclaringAssembly.ResolveIl2CppType(RawType.GetArrayElementType()),
        Il2CppTypeEnum.IL2CPP_TYPE_BYREF => throw new("TODO Support TYPE_BYREF"),
        _ => throw new($"Type {RawType.Type} is not a wrapper type")
    };

    public WrappedTypeAnalysisContext(Il2CppType rawType, AssemblyAnalysisContext referencedFrom) : base(rawType, referencedFrom)
    {
    }
}