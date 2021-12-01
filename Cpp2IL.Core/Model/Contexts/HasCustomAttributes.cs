using System.Collections.Generic;

namespace Cpp2IL.Core.Model.Contexts;

public abstract class HasCustomAttributes
{
    /// <summary>
    /// On V29, stores the custom attribute blob. Pre-29, stores the bytes for the custom attribute generator function.
    /// </summary>
    public byte[] Il2CppCustomAttributeData;

    /// <summary>
    /// Stores the analyzed custom attribute data once analysis has actually run.
    /// </summary>
    public List<AnalyzedCustomAttribute> CustomAttributes;

    public void AnalyzeCustomAttributeData()
    {
        
    }
}