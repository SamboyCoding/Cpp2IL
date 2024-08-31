using AssetRipper.Primitives;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.InstructionSets;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL;

namespace Cpp2IL.Core.Tests;

public static class GameLoader
{
    public static ApplicationAnalysisContext LoadSimpleGame()
    {
        TestGameLoader.LoadSimple2019Game();
        return Cpp2IlApi.CurrentAppContext;
    }
}
