using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Synthetic type for a value holding <c>Il2CppClass::rgctx_data</c>
/// </summary>
public class RgctxTableTypeAnalysisContext(TypeAnalysisContext ownerType, AssemblyAnalysisContext referencedFrom)
    : ReferencedTypeAnalysisContext(referencedFrom)
{
    /// <summary>The (usually inflated) type whose runtime generic context this is.</summary>
    public TypeAnalysisContext OwnerType { get; } = ownerType;

    public override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_I;

    public override string DefaultName => $"Il2CppRgctx<{OwnerType.FullName}>";

    public override string DefaultNamespace => "";

    public override bool IsValueType => false;
}
