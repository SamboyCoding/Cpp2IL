using System;
using System.Collections.Generic;
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
    /// The instruction set helper class associated with the instruction set that this application was compiled with.
    /// </summary>
    public BaseInstructionSet InstructionSet;
    
    /// <summary>
    /// All the managed assemblies contained within the metadata file.
    /// </summary>
    public readonly List<AssemblyAnalysisContext> Assemblies = new();

    public ApplicationAnalysisContext(Il2CppBinary binary, Il2CppMetadata metadata)
    {
        Binary = binary;
        Metadata = metadata;

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
    }
    
    /// <summary>
    /// Finds an assembly by its name and returns the analysis context for it.
    /// </summary>
    /// <param name="name">The name of the assembly (without any extension)</param>
    /// <returns>An assembly analysis context if one can be found which matches the given name, else null.</returns>
    public AssemblyAnalysisContext? GetAssemblyByName(string name)
    {
        return Assemblies.Find(assembly => assembly.Definition.AssemblyName.Name == name);
    }
}