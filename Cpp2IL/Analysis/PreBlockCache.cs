using System.Collections.Concurrent;
using Mono.Cecil;

namespace Cpp2IL
{
    internal partial class AsmDumper
    {
        private struct PreBlockCache
        {
            public ConcurrentDictionary<string, string> Aliases;
            public ConcurrentDictionary<string, TypeDefinition> Types;
            public ConcurrentDictionary<string, object> Constants;
            public BlockType BlockType;

            public PreBlockCache(ConcurrentDictionary<string, string> registerAliases, ConcurrentDictionary<string, object> registerContents, ConcurrentDictionary<string, TypeDefinition> registerTypes, BlockType type)
            {
                Aliases = new ConcurrentDictionary<string, string>();
                Types = new ConcurrentDictionary<string, TypeDefinition>();
                Constants = new ConcurrentDictionary<string, object>();

                foreach (var keyValuePair in registerAliases) Aliases[keyValuePair.Key] = keyValuePair.Value;
                foreach (var registerContent in registerContents) Constants[registerContent.Key] = registerContent.Value;
                foreach (var registerType in registerTypes) Types[registerType.Key] = registerType.Value;

                BlockType = type;
            }
        }
    }
}