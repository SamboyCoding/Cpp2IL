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
    /// The analysis context object for the manifest module of the assembly.
    /// </summary>
    public ModuleAnalysisContext ManifestModule { get; }
    
    /// <summary>
    /// The analysis context objects for all types exported by this assembly.
    /// </summary>
    public IEnumerable<TypeAnalysisContext> ExportedTypes => (Definition?.Image.ExportedTypes ?? []).Select(t => AppContext.ResolveContextForType(t)!);

    /// <summary>
    /// The code gen module for this assembly.
    ///
    /// Null prior to 24.2
    /// </summary>
    public Il2CppCodeGenModule? CodeGenModule;

    public virtual Version DefaultVersion
    {
        get
        {
            //handle __Generated assembly on v29, which has a version of 0.0.-1.-1
            return Definition is null || Definition.AssemblyName.build < 0
                ? new(0,0,0,0)
                : new(Definition.AssemblyName.major, Definition.AssemblyName.minor, Definition.AssemblyName.build, Definition.AssemblyName.revision);
        }
    }
    public Version? OverrideVersion { get; set; }
    public Version Version
    {
        get => OverrideVersion ?? DefaultVersion;
        set => OverrideVersion = value;
    }

    public virtual uint DefaultHashAlgorithm => Definition?.AssemblyName.hash_alg ?? default;
    public uint? OverrideHashAlgorithm { get; set; }
    public uint HashAlgorithm
    {
        get => OverrideHashAlgorithm ?? DefaultHashAlgorithm;
        set => OverrideHashAlgorithm = value;
    }

    public virtual uint DefaultFlags => Definition?.AssemblyName.flags ?? default;
    public uint? OverrideFlags { get; set; }
    public uint Flags
    {
        get => OverrideFlags ?? DefaultFlags;
        set => OverrideFlags = value;
    }

    public virtual string? DefaultCulture => Definition?.AssemblyName.Culture;
    /// <summary>
    /// Override <see cref="Culture"/>
    /// </summary>
    /// <remarks>
    /// <see langword="null""/> indicates no override, while an empty string indicates an explicit override to "no culture".
    /// </remarks>
    public string? OverrideCulture { get; set; }
    /// <summary>
    /// Gets or sets the culture
    /// </summary>
    /// <remarks>
    /// The get method will never return an empty string.
    /// </remarks>
    public string? Culture
    {
        get
        {
            var culture = OverrideCulture ?? DefaultCulture;
            return string.IsNullOrEmpty(culture) ? null : culture;
        }
        set
        {
            OverrideCulture = value is null ? "" : value;
        }
    }

    public virtual byte[]? DefaultPublicKeyToken => Definition?.AssemblyName.PublicKeyToken;
    /// <summary>
    /// Override <see cref="PublicKeyToken"/>
    /// </summary>
    /// <remarks>
    /// <see langword="null""/> indicates no override, while an empty array indicates an explicit override to "no public key token".
    /// </remarks>
    public byte[]? OverridePublicKeyToken { get; set; }
    /// <summary>
    /// Gets or sets the public key token
    /// </summary>
    /// <remarks>
    /// The get method will never return an empty array.
    /// </remarks>
    public byte[]? PublicKeyToken
    {
        get
        {
            var data = OverridePublicKeyToken ?? DefaultPublicKeyToken;
            return data is null || data.Length == 0 ? null : data;
        }
        set
        {
            OverridePublicKeyToken = value is null ? [] : value;
        }
    }

    public virtual byte[]? DefaultPublicKey => Definition?.AssemblyName.PublicKey;
    /// <summary>
    /// Override <see cref="PublicKey"/>
    /// </summary>
    /// <remarks>
    /// <see langword="null""/> indicates no override, while an empty array indicates an explicit override to "no public key".
    /// </remarks>
    public byte[]? OverridePublicKey { get; set; }
    /// <summary>
    /// Gets or sets the public key
    /// </summary>
    /// <remarks>
    /// The get method will never return an empty array.
    /// </remarks>
    public byte[]? PublicKey
    {
        get
        {
            var data = OverridePublicKey ?? DefaultPublicKey;
            return data is null || data.Length == 0 ? null : data;
        }
        set
        {
            OverridePublicKey = value is null ? [] : value;
        }
    }

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
        ManifestModule = new(this);

        if (assemblyDefinition is null)
            return;

        Definition = assemblyDefinition;

        if (AppContext.MetadataVersion >= 24.2f)
            CodeGenModule = AppContext.Binary.GetCodegenModuleByName(Definition.Image.Name!);

        InitCustomAttributeData();

        foreach (var il2CppTypeDefinition in Definition.Image.Types)
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

    public InjectedTypeAnalysisContext InjectType(string ns, string name, TypeAnalysisContext? baseType, TypeAttributes typeAttributes)
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
