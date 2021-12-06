using System.Collections.Generic;
using System.Linq;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents one managed type in the application.
/// </summary>
public class TypeAnalysisContext : HasCustomAttributes
{
    /// <summary>
    /// The context for the assembly this type was defined in.
    /// </summary>
    public readonly AssemblyAnalysisContext DeclaringAssembly;
    
    /// <summary>
    /// The underlying metadata for this type. Allows access to RGCTX data, the raw bitfield properties, interfaces, etc.
    /// </summary>
    public readonly Il2CppTypeDefinition Definition;
    
    /// <summary>
    /// The analysis contexts for methods contained within this type.
    /// </summary>
    public readonly List<MethodAnalysisContext> Methods = new();
    
    /// <summary>
    /// The analysis contexts for properties contained within this type.
    /// </summary>
    public readonly List<PropertyAnalysisContext> Properties = new();
    
    /// <summary>
    /// The analysis contexts for events contained within this type.
    /// </summary>
    public readonly List<EventAnalysisContext> Events = new();
    
    protected override int CustomAttributeIndex => Definition.customAttributeIndex;

    protected override AssemblyAnalysisContext CustomAttributeAssembly => DeclaringAssembly;

    public TypeAnalysisContext(Il2CppTypeDefinition il2CppTypeDefinition, AssemblyAnalysisContext parent) : base(il2CppTypeDefinition.token, parent.AppContext)
    {
        DeclaringAssembly = parent;
        Definition = il2CppTypeDefinition;
        
        InitCustomAttributeData();
        
        Methods = Definition.Methods!.Select(m => new MethodAnalysisContext(m, this)).ToList();
        Properties = Definition.Properties!.Select(p => new PropertyAnalysisContext(p, this)).ToList();
        Events = Definition.Events!.Select(e => new EventAnalysisContext(e, this)).ToList();
    }
    
    public MethodAnalysisContext? GetMethod(Il2CppMethodDefinition? methodDefinition)
    {
        if (methodDefinition == null)
            return null;
        
        return Methods.Find(m => m.Definition == methodDefinition);
    }
    
}