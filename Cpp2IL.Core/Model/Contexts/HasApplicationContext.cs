namespace Cpp2IL.Core.Model.Contexts;

/// <summary>
/// A base class to hold a reference to the application context that this object is part of.
/// </summary>
public abstract class HasApplicationContext(ApplicationAnalysisContext appContext) : ContextWithDataStorage
{
    /// <summary>
    /// The application context that this object is part of.
    /// </summary>
    public ApplicationAnalysisContext AppContext = appContext;
}
