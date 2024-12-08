using System;
using Cpp2IL.Core;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Attributes;
using Cpp2IL.Core.InstructionSets;
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

        Logger.VerboseNewline("\tRegistering built-in instruction set handlers...", "Core Plugin");

        InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_32);
        InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_64);
        InstructionSetRegistry.RegisterInstructionSet<WasmInstructionSet>(DefaultInstructionSets.WASM);
        InstructionSetRegistry.RegisterInstructionSet<ArmV7InstructionSet>(DefaultInstructionSets.ARM_V7);

        if (Environment.GetEnvironmentVariable("CPP2IL_LEGACY_ARM64") != null)
            InstructionSetRegistry.RegisterInstructionSet<Arm64InstructionSet>(DefaultInstructionSets.ARM_V8);
        else
            InstructionSetRegistry.RegisterInstructionSet<NewArmV8InstructionSet>(DefaultInstructionSets.ARM_V8);

        Logger.VerboseNewline("\tRegistering built-in binary parsers...", "Core Plugin");

        LibCpp2IlBinaryRegistry.RegisterBuiltInBinarySupport();

        Logger.VerboseNewline("\tRegistering built-in output formats...", "Core Plugin");

        OutputFormatRegistry.Register<AsmResolverDllOutputFormatLegacy>();
        OutputFormatRegistry.Register<AsmResolverDllOutputFormatDefault>();
        OutputFormatRegistry.Register<AsmResolverDllOutputFormatEmpty>();
        OutputFormatRegistry.Register<AsmResolverDllOutputFormatThrowNull>();
        OutputFormatRegistry.Register<AsmResolverDllOutputFormatIlRecovery>();
        OutputFormatRegistry.Register<DiffableCsOutputFormat>();
        OutputFormatRegistry.Register<IsilDumpOutputFormat>();
        OutputFormatRegistry.Register<WasmMappingOutputFormat>();
        OutputFormatRegistry.Register<WasmNameSectionOutputFormat>();

        Logger.VerboseNewline("\tRegistering built-in processing layers", "Core Plugin");

        ProcessingLayerRegistry.Register<AttributeAnalysisProcessingLayer>();
        ProcessingLayerRegistry.Register<AttributeInjectorProcessingLayer>();
        ProcessingLayerRegistry.Register<CallAnalysisProcessingLayer>();
        ProcessingLayerRegistry.Register<NativeMethodDetectionProcessingLayer>();
        ProcessingLayerRegistry.Register<StableRenamingProcessingLayer>();
        ProcessingLayerRegistry.Register<DeobfuscationMapProcessingLayer>();

        var elapsed = DateTime.Now - start;
        Logger.VerboseNewline($"Core plugin loaded in {elapsed.Ticks} ticks ({elapsed.TotalMilliseconds}ms)", "Core Plugin");
    }

    private sealed class AsmResolverDllOutputFormatLegacy : AsmResolverDllOutputFormatDefault
    {
        public override string OutputFormatId => "dummydll";
        public override string OutputFormatName => "DLL output format for backwards compatibility.";
    }
}
