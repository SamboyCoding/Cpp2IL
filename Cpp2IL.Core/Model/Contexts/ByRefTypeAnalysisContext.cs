using System;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class ByRefTypeAnalysisContext(TypeAnalysisContext elementType)
    : WrappedTypeAnalysisContext(elementType)
{
    public sealed override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_BYREF;

    public sealed override string DefaultName => $"{ElementType.DefaultName}&";

    public sealed override string? OverrideName
    {
        get => $"{ElementType.Name}&";
        set => throw new NotSupportedException();
    }

    public sealed override bool IsValueType => false;

    public override TypeAnalysisContext ElementType => base.ElementType ?? throw new("TODO Support TYPE_BYREF");
}
