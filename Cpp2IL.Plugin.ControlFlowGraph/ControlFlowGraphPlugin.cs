using Cpp2IL.Plugin.ControlFlowGraph;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Attributes;

[assembly: RegisterCpp2IlPlugin(typeof(ControlFlowGraphPlugin))]

namespace Cpp2IL.Plugin.ControlFlowGraph;

public class ControlFlowGraphPlugin : Cpp2IlPlugin
{
    public override string Name => "Control Flow Graph Plugin";
    public override string Description => "Adds an output format which generates control flow graph dot files for methods";
    public override void OnLoad()
    {
        OutputFormatRegistry.Register<ControlFlowGraphOutputFormat>();
        Logger.VerboseNewline("Control Flow Graph Plugin loaded and output format registered.");
    }
}
