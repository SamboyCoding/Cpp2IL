using System;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Api;

public abstract class Cpp2IlProcessingLayer
{
    /// <summary>
    /// The name for this processing layer, as displayed to the user (e.g. in log output, UI, etc.)
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// The ID for this processing layer, as used when specifying which ones to run from the command line. E.g. "attributeanalyzer"
    /// </summary>
    public abstract string Id { get; }

    /// <summary>
    /// Process on the given context. You can modify the context as you wish, but you should not store any state in your processing layer class itself - as it may be reused on other applications without warning. 
    /// </summary>
    /// <param name="appContext">The application context to process</param>
    /// <param name="progressCallback"
    /// >Optionally, a callback for progress updates. Takes two integer arguments - the number of steps done, and the number of steps to do.
    /// How you define steps is up to you - this is only used for providing user feedback.
    /// </param>
    public abstract void Process(ApplicationAnalysisContext appContext, Action<int, int>? progressCallback = null);
}