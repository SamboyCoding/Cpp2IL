using Mono.Cecil;

namespace Cpp2IL
{
    /// <summary>
    /// Represents the "klass" field of an il2cpp runtime object
    /// </summary>
    public struct Il2CppClassIdentifier
    {
        public TypeDefinition associatedDefinition;
        public string objectAlias;
    }
}