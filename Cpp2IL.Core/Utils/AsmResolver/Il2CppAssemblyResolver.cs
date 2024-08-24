using System.Collections.Generic;
using AsmResolver.DotNet;

namespace Cpp2IL.Core.Utils.AsmResolver;

internal class Il2CppAssemblyResolver : IAssemblyResolver
{
    internal readonly Dictionary<string, AssemblyDefinition> DummyAssemblies = new();

    public AssemblyDefinition? Resolve(AssemblyDescriptor assembly)
    {
        if (DummyAssemblies.TryGetValue(assembly.Name!, out var ret))
            return ret;

        return null;
    }

    public void AddToCache(AssemblyDescriptor descriptor, AssemblyDefinition definition)
    {
        //no-op
    }

    public bool RemoveFromCache(AssemblyDescriptor descriptor)
    {
        //no-op
        return true;
    }

    public bool HasCached(AssemblyDescriptor descriptor)
    {
        return true;
    }

    public void ClearCache()
    {
        //no-op
    }
}
