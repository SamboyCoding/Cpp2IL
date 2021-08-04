using System.Collections.Concurrent;
using System.Collections.Generic;
using Cpp2IL.Core.Analysis;
using LibCpp2IL.Metadata;
using Mono.Cecil;

namespace Cpp2IL.Core
{
    public static class SharedState
    {
        //Virt methods
        internal static readonly Dictionary<ushort, MethodDefinition> VirtualMethodsBySlot = new Dictionary<ushort, MethodDefinition>();
        
        //Methods
        internal static readonly ConcurrentDictionary<ulong, MethodDefinition> MethodsByAddress = new ConcurrentDictionary<ulong, MethodDefinition>();
        internal static readonly ConcurrentDictionary<long, MethodDefinition> MethodsByIndex = new ConcurrentDictionary<long, MethodDefinition>();
        internal static readonly ConcurrentDictionary<Il2CppMethodDefinition, MethodDefinition> UnmanagedToManagedMethods = new ConcurrentDictionary<Il2CppMethodDefinition, MethodDefinition>();
        internal static readonly ConcurrentDictionary<MethodDefinition, Il2CppMethodDefinition> ManagedToUnmanagedMethods = new ConcurrentDictionary<MethodDefinition, Il2CppMethodDefinition>();

        //Generic params
        internal static readonly Dictionary<long, GenericParameter> GenericParamsByIndex = new Dictionary<long, GenericParameter>();
        
        //Type defs
        internal static readonly ConcurrentDictionary<long, TypeDefinition> TypeDefsByIndex = new ConcurrentDictionary<long, TypeDefinition>();
        internal static readonly List<TypeDefinition> AllTypeDefinitions = new List<TypeDefinition>();
        internal static readonly ConcurrentDictionary<TypeDefinition, Il2CppTypeDefinition> ManagedToUnmanagedTypes = new ConcurrentDictionary<TypeDefinition, Il2CppTypeDefinition>();
        internal static readonly ConcurrentDictionary<Il2CppTypeDefinition, TypeDefinition> UnmanagedToManagedTypes = new ConcurrentDictionary<Il2CppTypeDefinition, TypeDefinition>();

        internal static readonly Dictionary<Il2CppTypeDefinition, Il2CppTypeDefinition> ConcreteImplementations = new Dictionary<Il2CppTypeDefinition, Il2CppTypeDefinition>();

        //Fields
        internal static readonly ConcurrentDictionary<Il2CppFieldDefinition, FieldDefinition> UnmanagedToManagedFields = new ConcurrentDictionary<Il2CppFieldDefinition, FieldDefinition>();
        internal static readonly ConcurrentDictionary<FieldDefinition, Il2CppFieldDefinition> ManagedToUnmanagedFields = new ConcurrentDictionary<FieldDefinition, Il2CppFieldDefinition>();
        internal static readonly ConcurrentDictionary<TypeDefinition, List<FieldInType>> FieldsByType = new ConcurrentDictionary<TypeDefinition, List<FieldInType>>();
        
        //Properties
        internal static readonly ConcurrentDictionary<Il2CppPropertyDefinition, PropertyDefinition> UnmanagedToManagedProperties = new ConcurrentDictionary<Il2CppPropertyDefinition, PropertyDefinition>();
        
        //Assemblies
        internal static readonly List<AssemblyDefinition> AssemblyList = new List<AssemblyDefinition>();
        internal static readonly Dictionary<AssemblyDefinition, Il2CppImageDefinition> ManagedToUnmanagedAssemblies = new Dictionary<AssemblyDefinition, Il2CppImageDefinition>();

        internal static void Clear()
		{
            VirtualMethodsBySlot.Clear();

            MethodsByAddress.Clear();
            MethodsByIndex.Clear();
            UnmanagedToManagedMethods.Clear();
            ManagedToUnmanagedMethods.Clear();

            GenericParamsByIndex.Clear();

            TypeDefsByIndex.Clear();
            AllTypeDefinitions.Clear();
            ManagedToUnmanagedTypes.Clear();
            UnmanagedToManagedTypes.Clear();

            ConcreteImplementations.Clear();

            UnmanagedToManagedFields.Clear();
            ManagedToUnmanagedFields.Clear();
            FieldsByType.Clear();

            UnmanagedToManagedProperties.Clear();

            AssemblyList.Clear();
            ManagedToUnmanagedAssemblies.Clear();
        }
    }
}