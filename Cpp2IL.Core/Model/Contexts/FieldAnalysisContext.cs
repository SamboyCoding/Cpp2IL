using System.Reflection;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;
using StableNameDotNet.Providers;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents a field in a managed type.
/// </summary>
public class FieldAnalysisContext : HasCustomAttributesAndName, IFieldInfoProvider
{
    /// <summary>
    /// The analysis context for the type that this field belongs to.
    /// </summary>
    public readonly TypeAnalysisContext DeclaringType;

    /// <summary>
    /// The underlying field metadata.
    /// </summary>
    public readonly Il2CppFieldReflectionData? BackingData;

    protected override int CustomAttributeIndex => BackingData?.Field.customAttributeIndex ?? -1;

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringType.DeclaringAssembly;

    public override string DefaultName => BackingData?.Field.Name!;

    public Il2CppType? FieldType => BackingData?.Field.RawFieldType;

    public virtual FieldAttributes Attributes => BackingData!.Attributes;

    public bool IsStatic => Attributes.HasFlag(FieldAttributes.Static);

    public int Offset => BackingData == null ? 0 : AppContext.Binary.GetFieldOffsetFromIndex(DeclaringType.Definition!.TypeIndex, BackingData.IndexInParent, BackingData.Field.FieldIndex, DeclaringType.Definition.IsValueType, IsStatic);

    public virtual TypeAnalysisContext FieldTypeContext => DeclaringType.DeclaringAssembly.ResolveIl2CppType(FieldType)
                                                           ?? throw new($"Field type {FieldType} could not be resolved.");


    public FieldAnalysisContext(Il2CppFieldReflectionData? backingData, TypeAnalysisContext parent) : base(backingData?.Field.token ?? 0, parent.AppContext)
    {
        DeclaringType = parent;
        BackingData = backingData;

        if (BackingData != null)
            InitCustomAttributeData();
    }

    public override string ToString() => $"Field: {DeclaringType.Definition?.Name}::{BackingData?.Field.Name}";

    #region StableNameDotNet

    public ITypeInfoProvider FieldTypeInfoProvider
        => GetGenericParamName(FieldTypeContext) is { } name
            ? new GenericParameterTypeInfoProviderWrapper(name)
            : TypeAnalysisContext.GetSndnProviderForType(AppContext, FieldType!);

    public string FieldName => Name;

    public FieldAttributes FieldAttributes => Attributes;

    private static string? GetGenericParamName(TypeAnalysisContext type) => type switch
    {
        GenericParameterTypeAnalysisContext genericParam => genericParam.Name,
        WrappedTypeAnalysisContext wrapped => GetGenericParamName(wrapped.ElementType),
        _ => null
    };

    #endregion
}
