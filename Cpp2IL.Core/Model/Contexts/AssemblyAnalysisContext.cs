using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents a single Assembly that was converted using IL2CPP.
/// </summary>
public class AssemblyAnalysisContext : HasCustomAttributesAndName
{
    /// <summary>
    /// The raw assembly metadata, such as its name, version, etc.
    /// </summary>
    public Il2CppAssemblyDefinition? Definition { get; set; }

    /// <summary>
    /// The analysis context objects for all types contained within the assembly, including those nested within a parent type.
    /// </summary>
    public List<TypeAnalysisContext> Types = [];

    /// <summary>
    /// The analysis context objects for all types contained within the assembly which are not nested within a parent type.
    /// </summary>
    public IEnumerable<TypeAnalysisContext> TopLevelTypes => Types.Where(t => t.DeclaringType == null);

    /// <summary>
    /// The code gen module for this assembly.
    ///
    /// Null prior to 24.2
    /// </summary>
    public Il2CppCodeGenModule? CodeGenModule;

    public virtual Version Version
    {
        get
        {
            //handle __Generated assembly on v29, which has a version of 0.0.-1.-1
            return Definition is null || Definition.AssemblyName.build < 0
                ? new(0,0,0,0)
                : new(Definition.AssemblyName.major, Definition.AssemblyName.minor, Definition.AssemblyName.build, Definition.AssemblyName.revision);
        }
    }

    public virtual uint HashAlgorithm => Definition?.AssemblyName.hash_alg ?? default;

    public virtual uint Flags => Definition?.AssemblyName.flags ?? default;

    public virtual string? Culture => Definition?.AssemblyName.Culture;

    public virtual byte[]? PublicKeyToken => Definition?.AssemblyName.PublicKeyToken;

    public virtual byte[]? PublicKey => Definition?.AssemblyName.PublicKey;

    protected override int CustomAttributeIndex => Definition?.CustomAttributeIndex ?? -1;

    public override AssemblyAnalysisContext CustomAttributeAssembly => this;

    private readonly Dictionary<string, TypeAnalysisContext> TypesByName = new();

    private readonly Dictionary<Il2CppTypeDefinition, TypeAnalysisContext> TypesByDefinition = new();

    public override string DefaultName => Definition?.AssemblyName.Name ?? throw new($"Injected assemblies should override {nameof(DefaultName)}");

    protected override bool IsInjected => Definition is null;

    /// <summary>
    /// Get assembly name without the extension and with any invalid path characters or elements removed.
    /// </summary>
    public string CleanAssemblyName => MiscUtils.CleanPathElement(Name);

    public AssemblyAnalysisContext(Il2CppAssemblyDefinition? assemblyDefinition, ApplicationAnalysisContext appContext) : base(assemblyDefinition?.Token ?? 0, appContext)
    {
        if (assemblyDefinition is null)
            return;

        Definition = assemblyDefinition;

        if (AppContext.MetadataVersion >= 24.2f)
            CodeGenModule = AppContext.Binary.GetCodegenModuleByName(Definition.Image.Name!);

        InitCustomAttributeData();

        foreach (var il2CppTypeDefinition in Definition.Image.Types!)
        {
            var typeContext = new TypeAnalysisContext(il2CppTypeDefinition, this);
            Types.Add(typeContext);
            TypesByName[il2CppTypeDefinition.FullName!] = typeContext;
            TypesByDefinition[il2CppTypeDefinition] = typeContext;
        }

        foreach (var type in Types)
        {
            if (type.Definition!.NestedTypeCount < 1)
                continue;

            type.NestedTypes = type.Definition.NestedTypes!.Select(n => GetTypeByFullName(n.FullName!) ?? throw new($"Unable to find nested type by name {n.FullName}"))
                .Peek(t => t.DeclaringType = type)
                .ToList();
        }
    }

    public TypeAnalysisContext InjectType(string ns, string name, TypeAnalysisContext? baseType, TypeAttributes typeAttributes = TypeAnalysisContext.DefaultTypeAttributes)
    {
        var ret = new InjectedTypeAnalysisContext(this, ns, name, baseType, typeAttributes);
        InjectType(ret);
        return ret;
    }

    internal void InjectType(InjectedTypeAnalysisContext ret)
    {
        Types.Add(ret);
        TypesByName[ret.FullName] = ret;
    }

    public TypeAnalysisContext? GetTypeByFullName(string fullName) => TypesByName.TryGetValue(fullName, out var typeContext) ? typeContext : null;

    public TypeAnalysisContext? GetTypeByDefinition(Il2CppTypeDefinition typeDefinition) => TypesByDefinition.TryGetValue(typeDefinition, out var typeContext) ? typeContext : null;

    public override string ToString() => "Assembly: " + Name;
}
