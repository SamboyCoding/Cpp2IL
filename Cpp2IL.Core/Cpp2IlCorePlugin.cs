using Cpp2IL.Core;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Attributes;
using Cpp2IL.Core.OutputFormats;
using Cpp2IL.Core.ProcessingLayers;
//Need this for the assembly attribute definition below.
using LibCpp2IL;

[assembly: RegisterCpp2IlPlugin(typeof(Cpp2IlCorePlugin))]

namespace Cpp2IL.Core;

public class Cpp2IlCorePlugin : Cpp2IlPlugin
{
    public override string Name => "Cpp2IL Built-In";
    
    public override string Description => "Core Cpp2IL plugin containing built-in instruction sets, binaries, and other core functionality.";
    
    public override void OnLoad()
    {
        Logger.VerboseNewline("Initializing...", "Core Plugin");
        var start = DateTime.Now;

        Logger.VerboseNewline("\tRegistering built-in binary parsers...", "Core Plugin");
        
        LibCpp2IlBinaryRegistry.RegisterBuiltInBinarySupport();
        
        Logger.VerboseNewline("\tRegistering built-in output formats...", "Core Plugin");
        
        OutputFormatRegistry.Register<AsmResolverDummyDllOutputFormat>();
        OutputFormatRegistry.Register<DiffableCsOutputFormat>();
        OutputFormatRegistry.Register<IsilDumpOutputFormat>();
        
        Logger.VerboseNewline("\tRegistering built-in processing layers", "Core Plugin");
        
        ProcessingLayerRegistry.Register<AttributeAnalysisProcessingLayer>();
        ProcessingLayerRegistry.Register<AttributeInjectorProcessingLayer>();
        ProcessingLayerRegistry.Register<CallAnalysisProcessingLayer>();
        ProcessingLayerRegistry.Register<StableRenamingProcessingLayer>();
        ProcessingLayerRegistry.Register<DeobfuscationMapProcessingLayer>();

        var elapsed = DateTime.Now - start;
        Logger.VerboseNewline($"Core plugin loaded in {elapsed.Ticks} ticks ({elapsed.TotalMilliseconds}ms)", "Core Plugin");
    }
}
