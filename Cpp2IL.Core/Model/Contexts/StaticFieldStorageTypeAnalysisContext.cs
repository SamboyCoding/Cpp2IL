using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Synthetic type for a value holding <c>Il2CppClass::static_fields</c>)
/// </summary>
public class StaticFieldStorageTypeAnalysisContext(TypeAnalysisContext ownerType, AssemblyAnalysisContext referencedFrom)
    : ReferencedTypeAnalysisContext(referencedFrom)
{
    /// <summary>The type whose static fields live in this storage block.</summary>
    public TypeAnalysisContext OwnerType { get; } = ownerType;

    public override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_I;

    public override string DefaultName => $"Il2CppStaticFields<{OwnerType.FullName}>";

    public override string DefaultNamespace => "";

    public override bool IsValueType => false;
}
