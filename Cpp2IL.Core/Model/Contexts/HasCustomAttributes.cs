using System.Collections.Generic;
using Cpp2IL.Core.Model.CustomAttributes;

namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// A base class to represent any type which has, or can have, custom attributes.
/// </summary>
public abstract class HasCustomAttributes : HasApplicationContext
{
    /// <summary>
    /// On V29, stores the custom attribute blob. Pre-29, stores the bytes for the custom attribute generator function.
    /// </summary>
    public byte[] Il2CppCustomAttributeData;

    /// <summary>
    /// Stores the analyzed custom attribute data once analysis has actually run.
    /// </summary>
    public List<AnalyzedCustomAttribute> CustomAttributes;

    /// <summary>
    /// Attempt to parse the Il2CppCustomAttributeData blob into custom attributes.
    /// </summary>
    public void AnalyzeCustomAttributeData()
    {
        
    }

    protected HasCustomAttributes(ApplicationAnalysisContext appContext) : base(appContext)
    {
    }
}