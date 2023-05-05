using AssetRipper.VersionUtilities;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.InstructionSets.All;
using LibCpp2IL;

namespace Cpp2IL.Core.Tests;

public static class GameLoader
{
    static GameLoader()
    {
        AllInstructionSets.Register();
        LibCpp2IlBinaryRegistry.RegisterBuiltInBinarySupport();
    }

    public static ApplicationAnalysisContext LoadSimpleGame()
    {
        Cpp2IlApi.InitializeLibCpp2Il(Paths.SimpleGame.GameAssembly, Paths.SimpleGame.Metadata, new UnityVersion(2019, 4, 34, UnityVersionType.Final, 1));
        return Cpp2IlApi.CurrentAppContext;
    }
}
