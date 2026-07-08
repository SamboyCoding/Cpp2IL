using System;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class CustomModifierTypeAnalysisContext(TypeAnalysisContext elementType, TypeAnalysisContext modifierType, bool required)
    : WrappedTypeAnalysisContext(elementType)
{
    public TypeAnalysisContext ModifierType { get; } = modifierType;

    public bool Required { get; } = required;

    public sealed override Il2CppTypeEnum Type => Required ? Il2CppTypeEnum.IL2CPP_TYPE_CMOD_REQD : Il2CppTypeEnum.IL2CPP_TYPE_CMOD_OPT;

    public sealed override string DefaultName => Required
        ? $"{ElementType.DefaultName} modreq({ModifierType.DefaultFullName})"
        : $"{ElementType.DefaultName} modopt({ModifierType.DefaultFullName})";

    public sealed override string? OverrideName
    {
        get => Required
            ? $"{ElementType.Name} modreq({ModifierType.FullName})"
            : $"{ElementType.Name} modopt({ModifierType.FullName})";
        set => throw new NotSupportedException();
    }

    public sealed override bool IsValueType => ElementType.IsValueType;
}
