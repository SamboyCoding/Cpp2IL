using System;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class PointerTypeAnalysisContext(TypeAnalysisContext elementType)
    : WrappedTypeAnalysisContext(elementType)
{
    public PointerTypeAnalysisContext(Il2CppType rawType, ApplicationAnalysisContext context)
        : this(context.ResolveIl2CppType(rawType.GetEncapsulatedType()))
    {
    }

    public sealed override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_PTR;

    public sealed override string DefaultName => $"{ElementType.DefaultName}*";

    public sealed override string? OverrideName
    {
        get => $"{ElementType.Name}*";
        set => throw new NotSupportedException();
    }

    public sealed override bool IsValueType => false;
}
