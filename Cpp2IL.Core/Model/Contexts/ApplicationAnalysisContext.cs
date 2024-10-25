using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Exceptions;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.Logging;
using LibCpp2IL;
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
    public Il2CppBinary Binary;

    /// <summary>
    /// The IL2CPP global-metadata file this application was loaded from.
    /// </summary>
    public Il2CppMetadata Metadata;

    /// <summary>
    /// The version of the IL2CPP metadata file this application was loaded from.
    /// </summary>
    public readonly float MetadataVersion;

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

    public ApplicationAnalysisContext(Il2CppBinary binary, Il2CppMetadata metadata, float metadataVersion)
    {
        Binary = binary;
        Metadata = metadata;
        MetadataVersion = metadataVersion;

        try
        {
            InstructionSet = InstructionSetRegistry.GetInstructionSet(binary.InstructionSetId);
        }
        catch (Exception)
        {
            throw new InstructionSetHandlerNotRegisteredException(binary.InstructionSetId);
        }

        Logger.VerboseNewline("\tUsing instruction set handler: " + InstructionSet.GetType().FullName);

        foreach (var assemblyDefinition in Metadata.AssemblyDefinitions)
        {
            Logger.VerboseNewline($"\tProcessing assembly: {assemblyDefinition.AssemblyName.Name}...");
            var aac = new AssemblyAnalysisContext(assemblyDefinition, this);
            Assemblies.Add(aac);
            AssembliesByName[assemblyDefinition.AssemblyName.Name] = aac;
        }

        SystemTypes = new(this);

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

    public TypeAnalysisContext? ResolveContextForType(Il2CppTypeDefinition? typeDefinition)
    {
        return typeDefinition is not null
            ? GetAssemblyByName(typeDefinition.DeclaringAssembly!.Name!)?.GetTypeByDefinition(typeDefinition)
            : null;
    }

    public MethodAnalysisContext? ResolveContextForMethod(Il2CppMethodDefinition? methodDefinition)
    {
        return ResolveContextForType(methodDefinition?.DeclaringType)?.Methods.FirstOrDefault(m => m.Definition == methodDefinition);
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

    public BaseKeyFunctionAddresses GetOrCreateKeyFunctionAddresses()
    {
        lock (InstructionSet)
        {
            if (_keyFunctionAddresses == null)
                (_keyFunctionAddresses = InstructionSet.CreateKeyFunctionAddressesInstance()).Find(this);

            return _keyFunctionAddresses;
        }
    }

    public MultiAssemblyInjectedType InjectTypeIntoAllAssemblies(string ns, string name, TypeAnalysisContext? baseType)
    {
        var types = Assemblies.Select(a => (InjectedTypeAnalysisContext)a.InjectType(ns, name, baseType)).ToArray();

        return new(types);
    }

    public IEnumerable<TypeAnalysisContext> AllTypes => Assemblies.SelectMany(a => a.Types);
}
