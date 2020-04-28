using System.Collections.Generic;
using Mono.Cecil;

namespace Cpp2IL
{
    public static class SharedState
    {
        internal static Dictionary<ushort, MethodDefinition> VirtualMethodsBySlot = new Dictionary<ushort, MethodDefinition>();
        
        internal static Dictionary<long, TypeDefinition> TypeDefsByAddress = new Dictionary<long, TypeDefinition>();
        internal static Dictionary<long, MethodDefinition> MethodsByIndex = new Dictionary<long, MethodDefinition>();

        internal static Dictionary<ulong, MethodDefinition> MethodsByAddress = new Dictionary<ulong, MethodDefinition>();

        internal static Dictionary<long, GenericParameter> GenericParamsByIndex = new Dictionary<long, GenericParameter>();
        
        internal static List<TypeDefinition> AllTypeDefinitions = new List<TypeDefinition>();
        
        internal static Dictionary<TypeDefinition, List<FieldInType>> FieldsByType = new Dictionary<TypeDefinition, List<FieldInType>>();
        
        internal static readonly List<AssemblyBuilder.GlobalIdentifier> Globals = new List<AssemblyBuilder.GlobalIdentifier>();
        
        internal static readonly Dictionary<ulong, AssemblyBuilder.GlobalIdentifier> GlobalsDict = new Dictionary<ulong, AssemblyBuilder.GlobalIdentifier>();
    }
}