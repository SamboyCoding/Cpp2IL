namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// Represents a single Module that was converted using IL2CPP.
/// </summary>
public class ModuleAnalysisContext : HasCustomAttributesAndName
{
    private AssemblyAnalysisContext _assemblyContext;
    
    /// <inheritdoc />
    override protected int CustomAttributeIndex => -1;
    /// <inheritdoc />
    public override AssemblyAnalysisContext CustomAttributeAssembly => _assemblyContext;
    /// <inheritdoc />
    public override string DefaultName => _assemblyContext.Definition!.Image.Name!;

    public ModuleAnalysisContext(AssemblyAnalysisContext asmCtx) : base(asmCtx.Definition?.ModuleToken ?? 1, asmCtx.AppContext)
    {
        _assemblyContext = asmCtx;
        
        InitCustomAttributeData();
    }
}
