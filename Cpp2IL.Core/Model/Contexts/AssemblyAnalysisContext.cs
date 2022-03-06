using System.Collections.Generic;
using System.Linq;
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

    public override string CustomAttributeOwnerName => Definition.AssemblyName.Name; 

    private Dictionary<string, TypeAnalysisContext> TypesByName = new();

    public AssemblyAnalysisContext(Il2CppAssemblyDefinition assemblyDefinition, ApplicationAnalysisContext appContext) : base(assemblyDefinition.Token, appContext)
    {
        Definition = assemblyDefinition;
        
        if (AppContext.MetadataVersion >= 24.2f)
            CodeGenModule = AppContext.Binary.GetCodegenModuleByName(Definition.Image.Name!);
        
        InitCustomAttributeData();
        
        foreach (var il2CppTypeDefinition in Definition.Image.Types!)
        {
            var typeContext = new TypeAnalysisContext(il2CppTypeDefinition, this);
            Types.Add(typeContext);
            TypesByName[il2CppTypeDefinition.FullName!] = typeContext;
        }

        foreach (var type in Types)
        {
            if(type.Definition.nested_type_count < 1)
                continue;
            
            type.NestedTypes = type.Definition.NestedTypes!.Select(n => GetTypeByFullName(n.FullName!) ?? throw new($"Unable to find nested type by name {n.FullName}")).ToList();
        }
    }

    public TypeAnalysisContext InjectType(string ns, string name)
    {
        var ret = new InjectedTypeAnalysisContext(this, name, ns);
        Types.Add(ret);
        return ret;
    }

    public TypeAnalysisContext? GetTypeByFullName(string fullName) => TypesByName.TryGetValue(fullName, out var typeContext) ? typeContext : null;
    
    public override string ToString() => "Assembly: " + Definition.AssemblyName.Name;
}