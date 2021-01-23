using System.Collections.Generic;
using Cpp2IL.Analysis;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using Mono.Cecil;

namespace Cpp2IL
{
    public static class SharedState
    {
        //Virt methods
        internal static readonly Dictionary<ushort, MethodDefinition> VirtualMethodsBySlot = new Dictionary<ushort, MethodDefinition>();
        
        //Methods
        internal static readonly Dictionary<ulong, MethodDefinition> MethodsByAddress = new Dictionary<ulong, MethodDefinition>();
        internal static readonly Dictionary<long, MethodDefinition> MethodsByIndex = new Dictionary<long, MethodDefinition>();
        internal static readonly Dictionary<Il2CppMethodDefinition, MethodDefinition> UnmanagedToManagedMethods = new Dictionary<Il2CppMethodDefinition, MethodDefinition>();

        //Generic params
        internal static readonly Dictionary<long, GenericParameter> GenericParamsByIndex = new Dictionary<long, GenericParameter>();
        
        //Type defs
        internal static readonly Dictionary<long, TypeDefinition> TypeDefsByIndex = new Dictionary<long, TypeDefinition>();
        internal static readonly List<TypeDefinition> AllTypeDefinitions = new List<TypeDefinition>();
        internal static readonly Dictionary<TypeDefinition, Il2CppTypeDefinition> MonoToCppTypeDefs = new Dictionary<TypeDefinition, Il2CppTypeDefinition>();
        internal static Dictionary<Il2CppTypeDefinition, TypeDefinition> UnmanagedToManagedTypes = new Dictionary<Il2CppTypeDefinition, TypeDefinition>();
        
        
        //Fields
        internal static readonly Dictionary<Il2CppFieldDefinition, FieldDefinition> UnmanagedToManagedFields = new Dictionary<Il2CppFieldDefinition, FieldDefinition>();
        internal static readonly Dictionary<TypeDefinition, List<FieldInType>> FieldsByType = new Dictionary<TypeDefinition, List<FieldInType>>();
        
        //Globals
        internal static readonly List<GlobalIdentifier> Globals = new List<GlobalIdentifier>();
        internal static readonly Dictionary<ulong, GlobalIdentifier> GlobalsByOffset = new Dictionary<ulong, GlobalIdentifier>();
    }
}