using System.Reflection;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents a field in a managed type.
/// </summary>
public class FieldAnalysisContext : HasCustomAttributesAndName
{
    /// <summary>
    /// The analysis context for the type that this field belongs to.
    /// </summary>
    public readonly TypeAnalysisContext DeclaringType;
    
    /// <summary>
    /// The underlying field metadata.
    /// </summary>
    public readonly Il2CppFieldReflectionData? BackingData;
    
    protected override int CustomAttributeIndex => BackingData?.field.customAttributeIndex ?? -1;

    protected internal  override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringType.DeclaringAssembly;

    public override string DefaultName => BackingData?.field.Name!;

    public virtual Il2CppType FieldType => BackingData!.field.RawFieldType!;
    
    public virtual FieldAttributes Attributes => BackingData!.attributes;
    
    public bool IsStatic => Attributes.HasFlag(FieldAttributes.Static);

    public int Offset => BackingData == null ? 0 : AppContext.Binary.GetFieldOffsetFromIndex(DeclaringType.Definition!.TypeIndex, BackingData.indexInParent, BackingData.field.FieldIndex, DeclaringType.Definition.IsValueType, IsStatic);
    

    public FieldAnalysisContext(Il2CppFieldReflectionData? backingData, TypeAnalysisContext parent) : base(backingData?.field.token ?? 0, parent.AppContext)
    {
        DeclaringType = parent;
        BackingData = backingData;
        
        if(BackingData != null)
            InitCustomAttributeData();
    }
    
    public override string ToString() => $"Field: {DeclaringType.Definition.Name}::{BackingData.field.Name}";
}