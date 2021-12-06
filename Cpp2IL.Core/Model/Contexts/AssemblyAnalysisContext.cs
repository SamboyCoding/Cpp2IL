using System.Collections.Generic;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents a single Assembly that was converted using IL2CPP.
/// </summary>
public class AssemblyAnalysisContext : HasCustomAttributes
{
    /// <summary>
    /// The raw assembly metadata, such as its name, version, etc.
    /// </summary>
    public Il2CppAssemblyDefinition Definition;
    
    /// <summary>
    /// The analysis context objects for types contained within the assembly.
    /// </summary>
    public List<TypeAnalysisContext> Types = new();
    
    /// <summary>
    /// The code gen module for this assembly.
    ///
    /// Null prior to 24.2
    /// </summary>
    public Il2CppCodeGenModule? CodeGenModule;
    
    protected override int CustomAttributeIndex => Definition.CustomAttributeIndex;

    protected override AssemblyAnalysisContext CustomAttributeAssembly => this;

    public AssemblyAnalysisContext(Il2CppAssemblyDefinition assemblyDefinition, ApplicationAnalysisContext appContext) : base(assemblyDefinition.Token, appContext)
    {
        Definition = assemblyDefinition;
        
        if (AppContext.MetadataVersion >= 24.2f)
            CodeGenModule = AppContext.Binary.GetCodegenModuleByName(Definition.Image.Name!);
        
        InitCustomAttributeData();
        
        foreach (var il2CppTypeDefinition in Definition.Image.Types!)
        {
            Types.Add(new(il2CppTypeDefinition, this));
        }
    }
}