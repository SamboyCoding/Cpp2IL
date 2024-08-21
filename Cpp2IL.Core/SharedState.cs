using System.Collections.Generic;
using Cpp2IL.Core.Utils;
using Cpp2IL.Core.Utils.AsmResolver;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core;

public static class SharedState
{
    internal static readonly Dictionary<Il2CppTypeDefinition, Il2CppTypeDefinition> ConcreteImplementations = new();

    internal static readonly HashSet<ulong> AttributeGeneratorStarts = [];

    internal static void Clear()
    {
            AsmResolverUtils.GenericParamsByIndexNew.Clear();

            AsmResolverUtils.TypeDefsByIndex.Clear();
            
            TypeDefinitionsAsmResolver.Reset();

            ConcreteImplementations.Clear();

            AttributeGeneratorStarts.Clear();
        }
}