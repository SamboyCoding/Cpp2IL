using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Api;

public abstract class Cpp2IlOutputFormat
{
    /// <summary>
    /// The ID of the output format as used when specifying an output format (e.g. in the command line)
    /// </summary>
    public abstract string OutputFormatId { get; }
    
    /// <summary>
    /// The name of the output format displayed to the user (e.g. in logs or the GUI)
    /// </summary>
    public abstract string OutputFormatName { get; }
    
    public abstract void DoOutput(ApplicationAnalysisContext context, string outputRoot);
}