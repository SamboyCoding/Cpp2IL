using System;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Attributes;
using Cpp2IL.Core.Model;
using LibCpp2IL;

[assembly: RegisterCpp2IlPlugin(typeof(Cpp2IL.Core.Cpp2IlCorePlugin))]

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
        
        Logger.VerboseNewline("\tRegistering built-in binary parsers...", "Core Plugin");
        
        LibCpp2IlBinaryRegistry.RegisterBuiltInBinarySupport();

        var elapsed = DateTime.Now - start;
        Logger.VerboseNewline($"Core plugin loaded in {elapsed.Ticks} ticks ({elapsed.TotalMilliseconds}ms)", "Core Plugin");
    }
}