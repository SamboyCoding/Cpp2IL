using System.Collections.Generic;
using Cpp2IL.Metadata;
using Mono.Cecil;

namespace Cpp2IL
{
    public static class SharedState
    {
        //Virt methods
        internal static Dictionary<ushort, MethodDefinition> VirtualMethodsBySlot = new Dictionary<ushort, MethodDefinition>();
        
        //Methods
        internal static Dictionary<ulong, MethodDefinition> MethodsByAddress = new Dictionary<ulong, MethodDefinition>();
        internal static Dictionary<long, MethodDefinition> MethodsByIndex = new Dictionary<long, MethodDefinition>();

        //Generic params
        internal static Dictionary<long, GenericParameter> GenericParamsByIndex = new Dictionary<long, GenericParameter>();
        
        //Type defs
        internal static Dictionary<long, TypeDefinition> TypeDefsByIndex = new Dictionary<long, TypeDefinition>();
        internal static List<TypeDefinition> AllTypeDefinitions = new List<TypeDefinition>();
        internal static Dictionary<TypeDefinition, Il2CppTypeDefinition> MonoToCppTypeDefs = new Dictionary<TypeDefinition, Il2CppTypeDefinition>();
        internal static Dictionary<Il2CppTypeDefinition, TypeDefinition> CppToMonoTypeDefs = new Dictionary<Il2CppTypeDefinition, TypeDefinition>();
        
        
        //Fields
        internal static Dictionary<TypeDefinition, List<FieldInType>> FieldsByType = new Dictionary<TypeDefinition, List<FieldInType>>();
        
        //Globals
        internal static readonly List<AssemblyBuilder.GlobalIdentifier> Globals = new List<AssemblyBuilder.GlobalIdentifier>();
        internal static readonly Dictionary<ulong, AssemblyBuilder.GlobalIdentifier> GlobalsDict = new Dictionary<ulong, AssemblyBuilder.GlobalIdentifier>();
    }
}