using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

// Synthetic type for a value that holds an IL2CPP runtime field pointer (<c>FieldInfo*</c>)
public class RuntimeFieldInfoAnalysisContext(FieldAnalysisContext representedField, AssemblyAnalysisContext referencedFrom)
    : ReferencedTypeAnalysisContext(referencedFrom)
{
    /// <summary>The field whose runtime FieldInfo this value points to.</summary>
    public FieldAnalysisContext RepresentedField { get; } = representedField;

    // A pointer-sized runtime handle; there is no Il2CppType enum value for the Il2CppFieldInfo struct.
    public override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_I;

    public override string DefaultName => "Il2CppFieldInfo";

    public override string DefaultNamespace => "";

    public override bool IsValueType => false;
}
