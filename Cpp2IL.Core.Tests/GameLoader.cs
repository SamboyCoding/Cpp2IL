using AssetRipper.VersionUtilities;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.InstructionSets;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL;

namespace Cpp2IL.Core.Tests;

public static class GameLoader
{
    static GameLoader()
    {
        InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_32);
        InstructionSetRegistry.RegisterInstructionSet<X86InstructionSet>(DefaultInstructionSets.X86_64);
        InstructionSetRegistry.RegisterInstructionSet<WasmInstructionSet>(DefaultInstructionSets.WASM);
        InstructionSetRegistry.RegisterInstructionSet<ArmV7InstructionSet>(DefaultInstructionSets.ARM_V7);
        var useNewArm64 = true;
        if (useNewArm64)
        {
            InstructionSetRegistry.RegisterInstructionSet<NewArmV8InstructionSet>(DefaultInstructionSets.ARM_V8);
        }
        else
        {
            InstructionSetRegistry.RegisterInstructionSet<Arm64InstructionSet>(DefaultInstructionSets.ARM_V8);
        }

        LibCpp2IlBinaryRegistry.RegisterBuiltInBinarySupport();
    }

    public static ApplicationAnalysisContext LoadSimpleGame()
    {
        Cpp2IlApi.InitializeLibCpp2Il(Paths.SimpleGame.GameAssembly, Paths.SimpleGame.Metadata, new UnityVersion(2019, 4, 34, UnityVersionType.Final, 1));
        return Cpp2IlApi.CurrentAppContext;
    }
}
