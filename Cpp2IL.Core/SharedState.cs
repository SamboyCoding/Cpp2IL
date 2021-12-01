using System.Collections.Concurrent;
using System.Collections.Generic;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core
{
    public static class SharedState
    {
        //Methods
        internal static readonly ConcurrentDictionary<Il2CppMethodDefinition, AsmResolver.DotNet.MethodDefinition> UnmanagedToManagedMethodsNew = new();
        internal static readonly ConcurrentDictionary<AsmResolver.DotNet.MethodDefinition, Il2CppMethodDefinition> ManagedToUnmanagedMethodsNew = new();

        //Generic params
        internal static readonly Dictionary<long, AsmResolver.DotNet.GenericParameter> GenericParamsByIndexNew = new();
        
        //Type defs
        internal static readonly ConcurrentDictionary<long, AsmResolver.DotNet.TypeDefinition> TypeDefsByIndexNew = new();
        internal static readonly List<AsmResolver.DotNet.TypeDefinition> AllTypeDefinitionsNew = new();
        internal static readonly ConcurrentDictionary<AsmResolver.DotNet.TypeDefinition, Il2CppTypeDefinition> ManagedToUnmanagedTypesNew = new();
        internal static readonly ConcurrentDictionary<Il2CppTypeDefinition, AsmResolver.DotNet.TypeDefinition> UnmanagedToManagedTypesNew = new();

        internal static readonly Dictionary<Il2CppTypeDefinition, Il2CppTypeDefinition> ConcreteImplementations = new();
        
        //Assemblies
        internal static readonly List<AsmResolver.DotNet.AssemblyDefinition> AssemblyList = new();
        
        internal static HashSet<ulong> AttributeGeneratorStarts = new();

        internal static void Clear()
        {
            UnmanagedToManagedMethodsNew.Clear();
            ManagedToUnmanagedMethodsNew.Clear();

            GenericParamsByIndexNew.Clear();

            TypeDefsByIndexNew.Clear();
            ManagedToUnmanagedTypesNew.Clear();
            UnmanagedToManagedTypesNew.Clear();

            ConcreteImplementations.Clear();

            AssemblyList.Clear();
            
            AttributeGeneratorStarts.Clear();
        }
    }
}