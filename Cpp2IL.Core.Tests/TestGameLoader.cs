using System.Diagnostics.CodeAnalysis;
using AssetRipper.Primitives;

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
    
    public static void LoadSimple2019Game()
    {
        EnsureInit();
        Cpp2IlApi.InitializeLibCpp2Il(Paths.Simple2019Game.GameAssembly, Paths.Simple2019Game.Metadata, new UnityVersion(2019, 4, 34, UnityVersionType.Final, 1));
    }
    
    public static void LoadSimple2022Game()
    {
        EnsureInit();
        Cpp2IlApi.InitializeLibCpp2Il(Paths.Simple2022Game.GameAssembly, Paths.Simple2022Game.Metadata, new UnityVersion(2022, 3, 35, UnityVersionType.Final, 1));
    }
}
