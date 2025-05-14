using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class CustomModifierTypeAnalysisContext(TypeAnalysisContext elementType, TypeAnalysisContext modifierType, bool required, AssemblyAnalysisContext referencedFrom)
    : WrappedTypeAnalysisContext(elementType, referencedFrom)
{
    public TypeAnalysisContext ModifierType { get; } = modifierType;

    public bool Required { get; } = required;

    public override Il2CppTypeEnum Type => Required ? Il2CppTypeEnum.IL2CPP_TYPE_CMOD_REQD : Il2CppTypeEnum.IL2CPP_TYPE_CMOD_OPT;

    public override string DefaultName => Required
        ? $"{ElementType.Name} modreq({ModifierType.Name})"
        : $"{ElementType.Name} modopt({ModifierType.Name})";

    public sealed override bool IsValueType => ElementType.IsValueType;
}
