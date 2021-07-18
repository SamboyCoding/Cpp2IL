using Mono.Cecil;

namespace Cpp2IL.Core
{
    public class RegistryAssemblyResolver : DefaultAssemblyResolver
    {
        public void Register(AssemblyDefinition assembly)
        {
            RegisterAssembly(assembly);
        }
    }
}