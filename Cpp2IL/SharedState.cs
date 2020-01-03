using System.Collections.Generic;
using Mono.Cecil;

namespace Cpp2IL
{
    public static class SharedState
    {
        internal static Dictionary<long, TypeDefinition> TypeDefsByAddress = new Dictionary<long, TypeDefinition>();
        internal static Dictionary<long, MethodDefinition> MethodsByIndex = new Dictionary<long, MethodDefinition>();

        internal static Dictionary<ulong, MethodDefinition> MethodsByAddress = new Dictionary<ulong, MethodDefinition>();

        internal static Dictionary<long, GenericParameter> GenericParamsByIndex = new Dictionary<long, GenericParameter>();
        
        internal static List<TypeDefinition> AllTypeDefinitions = new List<TypeDefinition>();
    }
}