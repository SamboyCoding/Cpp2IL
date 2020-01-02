using System.Collections.Generic;
using Mono.Cecil;

namespace Cpp2IL
{
    public static class SharedState
    {
        public static Dictionary<long, TypeDefinition> TypeDefsByAddress = new Dictionary<long, TypeDefinition>();
        public static Dictionary<long, MethodDefinition> MethodsByIndex = new Dictionary<long, MethodDefinition>();

        internal static Dictionary<ulong, MethodDefinition> MethodsByAddress = new Dictionary<ulong, MethodDefinition>();

        public static Dictionary<long, GenericParameter> GenericParamsByIndex = new Dictionary<long, GenericParameter>();
    }
}