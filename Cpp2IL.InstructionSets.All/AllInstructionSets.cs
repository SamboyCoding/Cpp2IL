using Cpp2IL.Core.Api;
using Cpp2IL.InstructionSets.ArmV7;
using Cpp2IL.InstructionSets.ArmV8;
using Cpp2IL.InstructionSets.Wasm;
using Cpp2IL.InstructionSets.X86;

namespace Cpp2IL.InstructionSets.All;

public static class AllInstructionSets
{
    public static void Register()
    {
        X86InstructionSet.RegisterInstructionSet();
        ArmV7InstructionSet.RegisterInstructionSet();
        ArmV8InstructionSet.RegisterInstructionSet();
        WasmInstructionSet.RegisterInstructionSet();
        OutputFormatRegistry.Register<WasmMappingOutputFormat>();
    }
}
