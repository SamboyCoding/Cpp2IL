using Mono.Cecil;

namespace Cpp2IL
{
    public class RegistryAssemblyResolver : DefaultAssemblyResolver
    {
        public void Register(AssemblyDefinition assembly)
        {
            RegisterAssembly(assembly);
        }
    }
}