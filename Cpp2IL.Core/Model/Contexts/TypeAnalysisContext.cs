using System.Collections.Generic;
using System.Linq;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents one managed type in the application.
/// </summary>
public class TypeAnalysisContext : HasCustomAttributesAndName
{
    /// <summary>
    /// The context for the assembly this type was defined in.
    /// </summary>
    public readonly AssemblyAnalysisContext DeclaringAssembly;

    /// <summary>
    /// The underlying metadata for this type. Allows access to RGCTX data, the raw bitfield properties, interfaces, etc.
    /// </summary>
    public readonly Il2CppTypeDefinition? Definition;

    /// <summary>
    /// The analysis contexts for methods contained within this type.
    /// </summary>
    public readonly List<MethodAnalysisContext> Methods;

    /// <summary>
    /// The analysis contexts for properties contained within this type.
    /// </summary>
    public readonly List<PropertyAnalysisContext> Properties;

    /// <summary>
    /// The analysis contexts for events contained within this type.
    /// </summary>
    public readonly List<EventAnalysisContext> Events;

    /// <summary>
    /// The analysis contexts for fields contained within this type.
    /// </summary>
    public readonly List<FieldAnalysisContext> Fields;

    /// <summary>
    /// The analysis contexts for nested types within this type.
    /// </summary>
    public List<TypeAnalysisContext> NestedTypes { get; internal set; } = new();

    protected override int CustomAttributeIndex => Definition!.CustomAttributeIndex;

    public override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringAssembly;

    public override string DefaultName => Definition?.Name! ?? throw new("Subclasses of TypeAnalysisContext must override DefaultName");

    public virtual string DefaultNs => Definition?.Namespace ?? throw new("Subclasses of TypeAnalysisContext must override DefaultNs");

    public string? OverrideNs { get; set; }

    public string Namespace => OverrideNs ?? DefaultNs;

    public TypeAnalysisContext? OverrideBaseType { get; protected set; }

    public TypeAnalysisContext(Il2CppTypeDefinition? il2CppTypeDefinition, AssemblyAnalysisContext containingAssembly) : base(il2CppTypeDefinition?.Token ?? 0, containingAssembly.AppContext)
    {
        DeclaringAssembly = containingAssembly;
        Definition = il2CppTypeDefinition;

        if (Definition != null)
        {
            InitCustomAttributeData();

            Methods = Definition.Methods!.Select(m => new MethodAnalysisContext(m, this)).ToList();
            Properties = Definition.Properties!.Select(p => new PropertyAnalysisContext(p, this)).ToList();
            Events = Definition.Events!.Select(e => new EventAnalysisContext(e, this)).ToList();
            Fields = Definition.FieldInfos!.ToList().Select(f => new FieldAnalysisContext(f, this)).ToList();
        }
        else
        {
            Methods = new();
            Properties = new();
            Events = new();
            Fields = new();
        }
    }

    public MethodAnalysisContext? GetMethod(Il2CppMethodDefinition? methodDefinition)
    {
        if (methodDefinition == null)
            return null;

        return Methods.Find(m => m.Definition == methodDefinition);
    }

    public List<MethodAnalysisContext> GetConstructors() => Methods.Where(m => m.Definition!.Name == ".ctor").ToList();

    public override string ToString() => $"Type: {Definition?.FullName}";
}