using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Api;

public abstract class Cpp2IlOutputFormat
{
    public abstract string OutputFormatId { get; }
    
    public abstract void DoOutput(ApplicationAnalysisContext context, string outputRoot);
}