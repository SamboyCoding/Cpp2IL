using System;
using System.IO;
using Cpp2IL.Core.Logging;
using LibCpp2IL;

namespace Cpp2IL.Core.Api;

public abstract class Cpp2IlPlugin
{
    protected readonly PluginLogger Logger;

    protected Cpp2IlPlugin()
    {
        Logger = new(this);
    }
    
    public abstract string Name { get; }
    public abstract string Description { get; }
    
    public abstract void OnLoad();

    protected void RegisterBinaryFormat<T>(string name, Func<byte[], bool> isValid, Func<MemoryStream, T> factory) where T : Il2CppBinary => 
        LibCpp2IlBinaryRegistry.Register(name, Name, isValid, factory);
}