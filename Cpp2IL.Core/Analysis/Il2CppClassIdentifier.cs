using LibCpp2IL.Metadata;

namespace Cpp2IL.Core.Analysis
{
    /// <summary>
    /// Represents the "klass" field of an il2cpp runtime object
    /// </summary>
    public class Il2CppClassIdentifier
    {
        public Il2CppTypeDefinition backingType;
        public string objectAlias;
    }
}