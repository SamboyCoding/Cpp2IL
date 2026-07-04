using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using AssetRipper.Primitives;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Exceptions;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Top-level class to represent an individual il2cpp application that has been loaded into cpp2il.
/// </summary>
public class ApplicationAnalysisContext : ContextWithDataStorage
{
    /// <summary>
    /// The IL2CPP binary file this application was loaded from
    /// </summary>
    public Il2CppBinary Binary => LibCpp2IlContext.Binary;

    /// <summary>
    /// The IL2CPP global-metadata file this application was loaded from.
    /// </summary>
    public Il2CppMetadata Metadata => LibCpp2IlContext.Metadata;

    /// <summary>
    /// The version of the IL2CPP metadata file this application was loaded from.
    /// </summary>
    public float MetadataVersion => Metadata.MetadataVersion;
    
    /// <summary>
    /// The Unity version this application was compiled with.
    /// </summary>
    public UnityVersion UnityVersion => Metadata.UnityVersion;

    /// <summary>
    /// The LibCpp2IlContext instance which this ApplicationAnalysisContext belongs to, containing the binary and metadata files that this application was loaded from.
    /// </summary>
    public LibCpp2IlContext LibCpp2IlContext;

    /// <summary>
    /// The instruction set helper class associated with the instruction set that this application was compiled with.
    /// </summary>
    public Cpp2IlInstructionSet InstructionSet;

    /// <summary>
    /// Contains references to some commonly-used System types.
    /// </summary>
    public SystemTypesContext SystemTypes;

    /// <summary>
    /// All the managed assemblies contained within the metadata file.
    /// </summary>
    public readonly List<AssemblyAnalysisContext> Assemblies = [];

    /// <summary>
    /// A dictionary of all the managed assemblies, by their name.
    /// </summary>
    public readonly Dictionary<string, AssemblyAnalysisContext> AssembliesByName = new();

    /// <summary>
    /// A dictionary of method pointers to the corresponding method, which may or may not be generic.
    /// </summary>
    public readonly Dictionary<ulong, List<MethodAnalysisContext>> MethodsByAddress = new();

    /// <summary>
    /// A dictionary of all the generic method variants to their corresponding analysis contexts.
    /// </summary>
    public readonly Dictionary<Cpp2IlMethodRef, ConcreteGenericMethodAnalysisContext> ConcreteGenericMethodsByRef = new();

    /// <summary>
    /// Key Function Addresses for the binary file. Populated on-demand
    /// </summary>
    private BaseKeyFunctionAddresses? _keyFunctionAddresses;

    /// <summary>
    /// True if this ApplicationAnalysisContext has finished initialization of all of its child contexts, else false.
    /// </summary>
    public bool HasFinishedInitializing { get; private set; }

    private readonly Dictionary<Il2CppImageDefinition, AssemblyAnalysisContext> AssembliesByImageDefinition = new();

    /// <summary>
    /// Cache for <see cref="GenericInstanceTypeAnalysisContext.GetOrCreate(Il2CppType, AssemblyAnalysisContext)"/>
    /// </summary>
    internal readonly ConcurrentDictionary<Il2CppType, Lazy<GenericInstanceTypeAnalysisContext>> GenericInstanceTypesByIl2CppType = new();

    public ApplicationAnalysisContext(LibCpp2IlContext context)
    {
        LibCpp2IlContext = context;

        try
        {
            InstructionSet = InstructionSetRegistry.GetInstructionSet(context.Binary.InstructionSetId);
        }
        catch (Exception)
        {
            throw new InstructionSetHandlerNotRegisteredException(context.Binary.InstructionSetId);
        }

        Logger.VerboseNewline("\tUsing instruction set handler: " + InstructionSet.GetType().FullName);

        foreach (var assemblyDefinition in Metadata.AssemblyDefinitions)
        {
            Logger.VerboseNewline($"\tProcessing assembly: {assemblyDefinition.AssemblyName.Name}...");
            var aac = new AssemblyAnalysisContext(assemblyDefinition, this);
            Assemblies.Add(aac);
            AssembliesByName[assemblyDefinition.AssemblyName.Name] = aac;
            AssembliesByImageDefinition[assemblyDefinition.Image] = aac;
        }

        SystemTypes = new(this);

        MiscUtils.InitFunctionStarts(this);

        PopulateMethodsByAddressTable();

        HasFinishedInitializing = true;
    }

    /// <summary>
    /// Populates the <see cref="MethodsByAddress"/> dictionary with all the methods in the application, including concrete generic ones.
    /// </summary>
    private void PopulateMethodsByAddressTable()
    {
        Assemblies.SelectMany(a => a.Types).SelectMany(t => t.Methods).ToList().ForEach(m =>
        {
            m.EnsureRawBytes();
            var ptr = InstructionSet.GetPointerForMethod(m);

            if (!MethodsByAddress.ContainsKey(ptr))
                MethodsByAddress.Add(ptr, []);

            MethodsByAddress[ptr].Add(m);
        });

        Logger.VerboseNewline("\tProcessing concrete generic methods...");
        foreach (var methodRef in Binary.ConcreteGenericMethods.Values.SelectMany(v => v))
        {
#if !DEBUG
            try
            {
#endif
            var gm = new ConcreteGenericMethodAnalysisContext(methodRef, this);

            var ptr = InstructionSet.GetPointerForMethod(gm);

            if (!MethodsByAddress.ContainsKey(ptr))
                MethodsByAddress[ptr] = [];

            MethodsByAddress[ptr].Add(gm);
            ConcreteGenericMethodsByRef[methodRef] = gm;
#if !DEBUG
            }
            catch (Exception e)
            {
                throw new("Failed to process concrete generic method: " + methodRef, e);
            }
#endif
        }
    }

    /// <summary>
    /// Finds an assembly by its name and returns the analysis context for it.
    /// </summary>
    /// <param name="name">The name of the assembly (without any extension)</param>
    /// <returns>An assembly analysis context if one can be found which matches the given name, else null.</returns>
    public AssemblyAnalysisContext? GetAssemblyByName(string name)
    {
        if (name.Length >= 4 && name[^4] == '.' && name[^3] == 'd')
            //Trim .dll extension
            name = name[..^4];
        else if (name.Length >= 6 && name[^6] == '.' && name[^5] == 'w')
            //Trim .winmd extension
            name = name[..^6];

        return AssembliesByName[name];
    }

    [return: NotNullIfNotNull(nameof(imageDefinition))]
    public AssemblyAnalysisContext? ResolveContextForAssembly(Il2CppImageDefinition? imageDefinition)
    {
        return imageDefinition is not null
            ? AssembliesByImageDefinition[imageDefinition]
            : null;
    }

    [return: NotNullIfNotNull(nameof(assemblyDefinition))]
    public AssemblyAnalysisContext? ResolveContextForAssembly(Il2CppAssemblyDefinition? assemblyDefinition)
    {
        return ResolveContextForAssembly(assemblyDefinition?.Image);
    }

    public TypeAnalysisContext? ResolveContextForType(Il2CppTypeDefinition? typeDefinition)
    {
        return typeDefinition is not null
            ? AssembliesByImageDefinition[typeDefinition.DeclaringAssembly!].GetTypeByDefinition(typeDefinition)
            : null;
    }

    public MethodAnalysisContext? ResolveContextForMethod(Il2CppMethodDefinition? methodDefinition)
    {
        return ResolveContextForType(methodDefinition?.DeclaringType)?.Methods.FirstOrDefault(m => m.Definition == methodDefinition);
    }

    [return: NotNullIfNotNull(nameof(methodReference))]
    public ConcreteGenericMethodAnalysisContext? ResolveContextForMethod(Cpp2IlMethodRef? methodReference)
    {
        if(methodReference == null)
            return null;
            
        return ConcreteGenericMethodsByRef.TryGetValue(methodReference, out var context) ? context : new(methodReference, this);
    }
    
    [return: NotNullIfNotNull(nameof(methodReference))]
    public MethodAnalysisContext? ResolveContextForMethod(MetadataUsage? methodReference)
    {
        return methodReference?.Type switch
        {
            MetadataUsageType.MethodDef => ResolveContextForMethod(methodReference.AsMethod()),
            MetadataUsageType.MethodRef => ResolveContextForMethod(methodReference.AsGenericMethodRef()),
            _ => null,
        };
    }

    public FieldAnalysisContext? ResolveContextForField(Il2CppFieldDefinition? field)
    {
        return ResolveContextForType(field?.DeclaringType)?.Fields.FirstOrDefault(f => f.BackingData?.Field == field);
    }

    public EventAnalysisContext? ResolveContextForEvent(Il2CppEventDefinition? eventDefinition)
    {
        return ResolveContextForType(eventDefinition?.DeclaringType)?.Events.FirstOrDefault(e => e.Definition == eventDefinition);
    }

    public PropertyAnalysisContext? ResolveContextForProperty(Il2CppPropertyDefinition? propertyDefinition)
    {
        return ResolveContextForType(propertyDefinition?.DeclaringType)?.Properties.FirstOrDefault(p => p.Definition == propertyDefinition);
    }

    public GenericParameterTypeAnalysisContext? ResolveContextForGenericParameter(Il2CppGenericParameter? genericParameter)
    {
        if (genericParameter is null)
            return null;

        if (genericParameter.Owner.TypeOwner is { } typeOwner)
        {
            return ResolveContextForType(typeOwner)?.GenericParameters[genericParameter.genericParameterIndexInOwner];
        }
        else
        {
            Debug.Assert(genericParameter.Owner.MethodOwner is not null);
            return ResolveContextForMethod(genericParameter.Owner.MethodOwner)?.GenericParameters[genericParameter.genericParameterIndexInOwner];
        }
    }

    public BaseKeyFunctionAddresses GetOrCreateKeyFunctionAddresses()
    {
        lock (InstructionSet)
        {
            if (_keyFunctionAddresses == null)
                (_keyFunctionAddresses = InstructionSet.CreateKeyFunctionAddressesInstance()).Find(this);

            return _keyFunctionAddresses;
        }
    }

    public MultiAssemblyInjectedType InjectTypeIntoAllAssemblies(string ns, string name, TypeAnalysisContext? baseType, TypeAttributes typeAttributes = TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed)
    {
        var types = Assemblies.Select(a => (InjectedTypeAnalysisContext)a.InjectType(ns, name, baseType, typeAttributes)).ToArray();

        return new(types);
    }

    public InjectedAssemblyAnalysisContext InjectAssembly(
        string name,
        Version? version = null,
        uint hashAlgorithm = 0,
        uint flags = 0,
        string? culture = null,
        byte[]? publicKeyToken = null,
        byte[]? publicKey = null)
    {
        var assembly = new InjectedAssemblyAnalysisContext(name, this, version, hashAlgorithm, flags, culture, publicKeyToken, publicKey);
        Assemblies.Add(assembly);
        AssembliesByName.Add(name, assembly);
        return assembly;
    }

    public IEnumerable<TypeAnalysisContext> AllTypes => Assemblies.SelectMany(a => a.Types);
}
