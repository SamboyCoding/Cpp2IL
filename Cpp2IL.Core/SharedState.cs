using System.Collections.Generic;
using Cpp2IL.Core.Utils;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core
{
    public static class SharedState
    {
        internal static readonly Dictionary<Il2CppTypeDefinition, Il2CppTypeDefinition> ConcreteImplementations = new();

        internal static readonly HashSet<ulong> AttributeGeneratorStarts = new();

        internal static void Clear()
        {
            AsmResolverUtils.GenericParamsByIndexNew.Clear();

            AsmResolverUtils.TypeDefsByIndex.Clear();

            ConcreteImplementations.Clear();

            AttributeGeneratorStarts.Clear();
        }
    }
}