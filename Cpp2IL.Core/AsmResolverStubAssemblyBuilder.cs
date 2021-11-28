using System;
using System.Collections.Generic;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.IO;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;
using LibCpp2IL.Metadata;
using AssemblyDefinition = AsmResolver.DotNet.AssemblyDefinition;

namespace Cpp2IL.Core
{
    public static class AsmResolverStubAssemblyBuilder
    {
        private class Il2CppAssemblyResolver : IAssemblyResolver
        {
            internal readonly Dictionary<string, AssemblyDefinition> DummyAssemblies = new();
            
            public AssemblyDefinition? Resolve(AssemblyDescriptor assembly)
            {
                if (DummyAssemblies.TryGetValue(assembly.Name!, out var ret))
                    return ret;

                return null;
            }

            public void AddToCache(AssemblyDescriptor descriptor, AssemblyDefinition definition)
            {
                //no-op
            }

            public bool RemoveFromCache(AssemblyDescriptor descriptor)
            {
                //no-op
                return true;
            }

            public bool HasCached(AssemblyDescriptor descriptor)
            {
                return true;
            }

            public void ClearCache()
            {
                //no-op
            }
        }

        public static List<AssemblyDefinition> BuildStubAssemblies(Il2CppMetadata metadata)
        {
            var assemblyResolver = new Il2CppAssemblyResolver();
            var metadataResolver = new DefaultMetadataResolver(assemblyResolver);
            
            var corlib = metadata.AssemblyDefinitions.First(a => a.AssemblyName.Name == "mscorlib");
            var managedCorlib = BuildStubAssembly(corlib, null, metadataResolver);
            assemblyResolver.DummyAssemblies.Add(managedCorlib.Name!, managedCorlib);

            var ret = metadata.AssemblyDefinitions
                // .AsParallel()
                .Where(a => a.AssemblyName.Name != "mscorlib")
                .Select(a => BuildStubAssembly(a, managedCorlib, metadataResolver))
                .ToList();
            
            ret.ForEach(a => assemblyResolver.DummyAssemblies.Add(a.Name!, a));
            
            ret.Add(managedCorlib);

            return ret;
        }

        private static AssemblyDefinition BuildStubAssembly(Il2CppAssemblyDefinition assemblyDefinition, AssemblyDefinition? corLib, DefaultMetadataResolver metadataResolver)
        {
            var imageDefinition = assemblyDefinition.Image;

            //Get the name of the assembly (= the name of the DLL without the file extension)
            var assemblyNameString = assemblyDefinition.AssemblyName.Name;

            //Build a Mono.Cecil assembly name from this name
            Version vers;
            if (assemblyDefinition.AssemblyName.build >= 0)
                //handle __Generated assembly on v29, which has a version of 0.0.-1.-1
                vers = new(assemblyDefinition.AssemblyName.major, assemblyDefinition.AssemblyName.minor, assemblyDefinition.AssemblyName.build, assemblyDefinition.AssemblyName.revision);
            else
                vers = new(0, 0, 0, 0);

            var ourAssembly = new AssemblyDefinition(assemblyNameString, vers)
            {
                HashAlgorithm = (AssemblyHashAlgorithm) assemblyDefinition.AssemblyName.hash_alg,
                Attributes = (AssemblyAttributes) assemblyDefinition.AssemblyName.flags,
                Culture = assemblyDefinition.AssemblyName.Culture,
                //TODO find a way to set hash? or not needed
            };

            //Setting the corlib module allows element types in references to that assembly to be set correctly without us having to manually set them.
            var managedModule = new ModuleDefinition(imageDefinition.Name, new(corLib ?? ourAssembly)) //Use either ourself as corlib, if we are corlib, otherwise the provided one
            {
                MetadataResolver = metadataResolver
            }; 
            ourAssembly.Modules.Add(managedModule);

            foreach (var il2CppTypeDefinition in imageDefinition.Types!.Where(t => t.DeclaringType == null))
            {
                managedModule.TopLevelTypes.Add(BuildStubType(il2CppTypeDefinition));
            }


            return ourAssembly;
        }

        private static TypeDefinition BuildStubType(Il2CppTypeDefinition type)
        {
            var ret = new TypeDefinition(type.Namespace, type.Name, (TypeAttributes) type.flags);

            ushort packingSize = 0;
            var classSize = 0U;
            if (!type.PackingSizeIsDefault)
                packingSize = (ushort) type.PackingSize;

            if (!type.ClassSizeIsDefault)
            {
                if (type.Size > 1 << 30)
                    throw new Exception($"Got invalid size for type {type}: {type.RawSizes}");

                if (type.Size != -1)
                    classSize = (uint) type.Size;
                else
                    classSize = 0; //Not sure what this value actually implies but it seems to work
            }

            if (packingSize != 0 || classSize != 0)
                ret.ClassLayout = new(packingSize, classSize);

            foreach (var cppNestedType in type.NestedTypes!)
            {
                ret.NestedTypes.Add(BuildStubType(cppNestedType));
            }

            SharedState.AllTypeDefinitionsNew.Add(ret);
            SharedState.ManagedToUnmanagedTypesNew[ret] = type;
            SharedState.UnmanagedToManagedTypesNew[type] = ret;
            SharedState.TypeDefsByIndexNew[type.TypeIndex] = ret;
            return ret;
        }
    }
}