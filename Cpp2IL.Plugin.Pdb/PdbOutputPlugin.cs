using Cpp2IL.Core.Api;
using Cpp2IL.Core.Attributes;
using Cpp2IL.Plugin.Pdb;

[assembly: RegisterCpp2IlPlugin(typeof(PdbOutputPlugin))]

namespace Cpp2IL.Plugin.Pdb;

public class PdbOutputPlugin : Cpp2IlPlugin
{
    public override string Name => "PDB Output Plugin";

    public override string Description => "Adds an output format which generates debug symbols";

    public override void OnLoad()
    {
        OutputFormatRegistry.Register<PdbOutputFormat>();
    }
}
