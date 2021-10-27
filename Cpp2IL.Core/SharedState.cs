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
        internal static readonly Dictionary<ushort, MethodDefinition> VirtualMethodsBySlot = new();
        
        //Methods
        internal static readonly ConcurrentDictionary<ulong, MethodDefinition> MethodsByAddress = new();
        internal static readonly ConcurrentDictionary<long, MethodDefinition> MethodsByIndex = new();
        internal static readonly ConcurrentDictionary<Il2CppMethodDefinition, MethodDefinition> UnmanagedToManagedMethods = new();
        internal static readonly ConcurrentDictionary<MethodDefinition, Il2CppMethodDefinition> ManagedToUnmanagedMethods = new();

        //Generic params
        internal static readonly Dictionary<long, GenericParameter> GenericParamsByIndex = new();
        
        //Type defs
        internal static readonly ConcurrentDictionary<long, TypeDefinition> TypeDefsByIndex = new();
        internal static readonly List<TypeDefinition> AllTypeDefinitions = new();
        internal static readonly ConcurrentDictionary<TypeDefinition, Il2CppTypeDefinition> ManagedToUnmanagedTypes = new();
        internal static readonly ConcurrentDictionary<Il2CppTypeDefinition, TypeDefinition> UnmanagedToManagedTypes = new();

        internal static readonly Dictionary<Il2CppTypeDefinition, Il2CppTypeDefinition> ConcreteImplementations = new();

        //Fields
        internal static readonly ConcurrentDictionary<Il2CppFieldDefinition, FieldDefinition> UnmanagedToManagedFields = new();
        internal static readonly ConcurrentDictionary<FieldDefinition, Il2CppFieldDefinition> ManagedToUnmanagedFields = new();
        internal static readonly ConcurrentDictionary<TypeDefinition, List<FieldInType>> FieldsByType = new();
        
        //Properties
        internal static readonly ConcurrentDictionary<Il2CppPropertyDefinition, PropertyDefinition> UnmanagedToManagedProperties = new();
        internal static readonly ConcurrentDictionary<PropertyDefinition, Il2CppPropertyDefinition> ManagedToUnmanagedProperties = new();
        
        //Assemblies
        internal static readonly List<AssemblyDefinition> AssemblyList = new();
        internal static readonly Dictionary<AssemblyDefinition, Il2CppImageDefinition> ManagedToUnmanagedAssemblies = new();
        
        internal static HashSet<ulong> AttributeGeneratorStarts = new();

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
            
            AttributeGeneratorStarts.Clear();
        }
    }
}