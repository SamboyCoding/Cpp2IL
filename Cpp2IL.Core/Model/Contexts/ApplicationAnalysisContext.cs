using System;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Exceptions;
using LibCpp2IL;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Top-level class to represent an individual il2cpp application that has been loaded into cpp2il.
/// </summary>
public class ApplicationAnalysisContext
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
    public BaseInstructionSet InstructionSet;
    
    /// <summary>
    /// All the managed assemblies contained within the metadata file.
    /// </summary>
    public readonly List<AssemblyAnalysisContext> Assemblies = new();
    
    /// <summary>
    /// A dictionary of method pointers to the corresponding method, which may or may not be generic.
    /// </summary>
    public readonly Dictionary<ulong, List<MethodAnalysisContext>> MethodsByAddress = new();

    /// <summary>
    /// Key Function Addresses for the binary file. Can be populated via <see cref="Cpp2IlApi.ScanForKeyFunctionAddresses"/>
    /// </summary>
    public BaseKeyFunctionAddresses KeyFunctionAddresses { get; internal set; }

    public ApplicationAnalysisContext(Il2CppBinary binary, Il2CppMetadata metadata, float metadataVersion)
    {
        Binary = binary;
        Metadata = metadata;
        MetadataVersion = metadataVersion;

        try
        {
            InstructionSet = InstructionSetRegistry.GetInstructionSet(binary.InstructionSetId);
        }
        catch (Exception e)
        {
            throw new InstructionSetHandlerNotRegisteredException(binary.InstructionSetId);
        }

        foreach (var assemblyDefinition in Metadata.AssemblyDefinitions) 
            Assemblies.Add(new(assemblyDefinition, this));
        
        PopulateMethodsByAddressTable();
    }

    /// <summary>
    /// Populates the <see cref="MethodsByAddress"/> dictionary with all the methods in the application, including concrete generic ones.
    /// </summary>
    private void PopulateMethodsByAddressTable()
    {
        Assemblies.SelectMany(a => a.Types).SelectMany(t => t.Methods).ToList().ForEach(m =>
        {
            var ptr = InstructionSet.GetPointerForMethod(m);

            if (!MethodsByAddress.ContainsKey(ptr))
                MethodsByAddress.Add(ptr, new());

            MethodsByAddress[ptr].Add(m);
        });

        foreach (var methodRef in Binary.ConcreteGenericMethods.Values.SelectMany(v => v))
        {
            var gm = new ConcreteGenericMethodAnalysisContext(methodRef, this);

            var ptr = InstructionSet.GetPointerForMethod(gm);

            if (!MethodsByAddress.ContainsKey(ptr))
                MethodsByAddress[ptr] = new();

            MethodsByAddress[ptr].Add(gm);
        }
    }

    /// <summary>
    /// Finds an assembly by its name and returns the analysis context for it.
    /// </summary>
    /// <param name="name">The name of the assembly (without any extension)</param>
    /// <returns>An assembly analysis context if one can be found which matches the given name, else null.</returns>
    public AssemblyAnalysisContext? GetAssemblyByName(string name)
    {
        if (name[^4] == '.' && name[^3] == 'd')
            //Trim .dll extension
            name = name[..^4];
        
        return Assemblies.Find(assembly => assembly.Definition.AssemblyName.Name == name);
    }

    public TypeAnalysisContext? ResolveContextForType(Il2CppTypeDefinition typeDefinition) => GetAssemblyByName(typeDefinition.DeclaringAssembly!.Name!)?.Types.Find(t => t.Definition == typeDefinition);
}