using AssetRipper.Primitives;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Tests;

public static class TestGameLoader
{
    private static bool _initialized;
    
    private static void EnsureInit()
    {
        if(!_initialized)
            Cpp2IlApi.Init();
        
        _initialized = true;
    }
    
    public static ApplicationAnalysisContext LoadSimple2019Game()
    {
        EnsureInit();
        Cpp2IlApi.InitializeLibCpp2Il(Paths.Simple2019Game.GameAssembly, Paths.Simple2019Game.Metadata, new UnityVersion(2019, 4, 34, UnityVersionType.Final, 1));
        return Cpp2IlApi.CurrentAppContext!;
    }
    
    public static ApplicationAnalysisContext LoadSimple2022Game()
    {
        EnsureInit();
        Cpp2IlApi.InitializeLibCpp2Il(Paths.Simple2022Game.GameAssembly, Paths.Simple2022Game.Metadata, new UnityVersion(2022, 3, 35, UnityVersionType.Final, 1));
        return Cpp2IlApi.CurrentAppContext!;
    }
    
    public static ApplicationAnalysisContext LoadSimpleV106Game()
    {
        EnsureInit();
        Cpp2IlApi.InitializeLibCpp2Il(Paths.SimpleV106Game.GameAssembly, Paths.SimpleV106Game.Metadata, new UnityVersion(6000, 5, 0, UnityVersionType.Alpha, 6));
        return Cpp2IlApi.CurrentAppContext!;
    }
}
