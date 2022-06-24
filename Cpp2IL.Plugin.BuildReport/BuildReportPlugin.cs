using Cpp2IL.Core.Api;
using Cpp2IL.Core.Attributes;
using Cpp2IL.Plugin.BuildReport;

[assembly: RegisterCpp2IlPlugin(typeof(BuildReportPlugin))]

namespace Cpp2IL.Plugin.BuildReport;

public class BuildReportPlugin : Cpp2IlPlugin
{
    public override string Name => "Build Report Plugin";
    public override string Description => "Adds an output format which generates information useful to the developer about what is taking up space in the build process";
    public override void OnLoad()
    {
        OutputFormatRegistry.Register<BuildReportOutputFormat>();
        Logger.VerboseNewline("Build Report Plugin loaded and output format registered.");
    }
}