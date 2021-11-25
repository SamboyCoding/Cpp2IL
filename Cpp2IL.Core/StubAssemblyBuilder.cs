using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Utils;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Cpp2IL.Core
{
    internal static class StubAssemblyBuilder
    {
        /// <summary>
        /// Creates all the Assemblies defined in the provided metadata, along with (stub) definitions of all the types contained therein.
        /// </summary>
        /// <param name="metadata">The Il2Cpp metadata to extract assemblies from</param>
        /// <param name="moduleParams">Configuration for the module creation.</param>
        /// <returns>A list of Mono.Cecil Assemblies, containing empty type definitions for each defined type.</returns>
        internal static List<AssemblyDefinition> BuildStubAssemblies(Il2CppMetadata metadata, ModuleParameters moduleParams)
        {
            SharedState.ManagedToUnmanagedAssemblies.Clear();
            
            return metadata.AssemblyDefinitions
                // .AsParallel()
                .Select(assemblyDefinition => BuildStubAssembly(moduleParams, assemblyDefinition))
                .ToList();
        }

        private static AssemblyDefinition BuildStubAssembly(ModuleParameters moduleParams, Il2CppAssemblyDefinition assemblyDefinition)
        {
            var imageDefinition = assemblyDefinition.Image;
            
            //Get the name of the assembly (= the name of the DLL without the file extension)
            var assemblyNameString = assemblyDefinition.AssemblyName.Name;

            //Build a Mono.Cecil assembly name from this name
            Version vers;
            if (assemblyDefinition.AssemblyName.build >= 0)
                //handle __Generated assembly on v29, which has a version of 0.0.-1.-1
                vers = new Version(assemblyDefinition.AssemblyName.major, assemblyDefinition.AssemblyName.minor, assemblyDefinition.AssemblyName.build, assemblyDefinition.AssemblyName.revision);
            else
                vers = new Version(0, 0, 0, 0);
            
            var asmName = new AssemblyNameDefinition(assemblyNameString, vers);
            asmName.HashAlgorithm = (AssemblyHashAlgorithm) assemblyDefinition.AssemblyName.hash_alg;
            asmName.Attributes = (AssemblyAttributes) assemblyDefinition.AssemblyName.flags;
            asmName.Culture = assemblyDefinition.AssemblyName.Culture;
            asmName.PublicKeyToken = BitConverter.GetBytes(assemblyDefinition.AssemblyName.publicKeyToken);
            if (assemblyDefinition.AssemblyName.publicKeyToken == 0)
                asmName.PublicKeyToken = Array.Empty<byte>();
            // asmName.PublicKey = Encoding.UTF8.GetBytes(assemblyDefinition.AssemblyName.PublicKey); //This seems to be garbage data, e.g. "\x0\x0\x0\x0\x0\x0\x0\x0\x4\x0\x0\x0\x0\x0\x0\x0", so we skip
            asmName.Hash = assemblyDefinition.AssemblyName.hash_len == 0 ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(assemblyDefinition.AssemblyName.HashValue);
            
            if(assemblyDefinition.Token != 0)
                asmName.MetadataToken = new MetadataToken(assemblyDefinition.Token);

            //Create an empty assembly and register it
            var assembly = AssemblyDefinition.CreateAssembly(asmName, imageDefinition.Name, moduleParams);

            //Ensure it really _is_ empty
            var mainModule = assembly.MainModule;
            mainModule.Types.Clear();

            //Populate types.
            foreach (var type in imageDefinition.Types!) 
                HandleTypeInAssembly(type, mainModule);

            SharedState.ManagedToUnmanagedAssemblies[assembly] = imageDefinition;

            return assembly;
        }

        private static void HandleTypeInAssembly(Il2CppTypeDefinition type, ModuleDefinition mainModule)
        {
            //Get the metadata type info, its namespace, and name.
            var ns = type.Namespace!;
            var name = type.Name!;

            TypeDefinition? definition = null;
            var isNestedType = type.declaringTypeIndex != -1; 
            if (isNestedType)
            {
                //This is a type declared within another (inner class/type)

                //Have we already declared this type due to handling its parent?
                SharedState.TypeDefsByIndex.TryGetValue(type.TypeIndex, out definition);
            }

            if (definition == null)
            {
                //This is a new type (including nested type with parent not defined yet) so ensure it's registered
                definition = new TypeDefinition(ns, name, (TypeAttributes) type.flags);
                if (ns == "System")
                {
                    var etype = name switch
                    {
                        //See ElementType in Mono.Cecil.Metadata
                        "Void" => CecilEType.Void,
                        nameof(Boolean) => CecilEType.Boolean,
                        nameof(Char) => CecilEType.Char,
                        nameof(SByte) => CecilEType.I1, //I1
                        nameof(Byte) => CecilEType.U1, //U1
                        nameof(Int16) => CecilEType.I2, //I2
                        nameof(UInt16) => CecilEType.U2, //U2
                        nameof(Int32) => CecilEType.I4, //I4
                        nameof(UInt32) => CecilEType.U4, //U4
                        nameof(Int64) => CecilEType.I8, //I8
                        nameof(UInt64) => CecilEType.U8, //U8
                        nameof(Single) => CecilEType.R4, //R4
                        nameof(Double) => CecilEType.R8, //R8
                        nameof(String) => CecilEType.String,
                        nameof(Object) => CecilEType.Object, //Object
                        // nameof(IntPtr) => 0xF,
                        _ => CecilEType.None,
                    };
                    
                    if(etype != 0)
                        //Fixup internaL cecil etypes for (among other things) attribute blobs.
                        definition.SetEType(etype);
                }

                if(!isNestedType)
                    mainModule.Types.Add(definition);

                if (!type.PackingSizeIsDefault)
                    definition.PackingSize = (short)type.PackingSize;
                if (!type.ClassSizeIsDefault)
                {
                    if (type.Size > 1 << 30)
                        throw new Exception($"Got invalid size for type {type}: {type.RawSizes}");

                    if (type.Size != -1)
                        definition.ClassSize = type.Size;
                    else
                        definition.ClassSize = 0; //Not sure what this value actually implies but it seems to work
                }

                SharedState.AllTypeDefinitions.Add(definition);
                SharedState.TypeDefsByIndex[type.TypeIndex] = definition;
                SharedState.UnmanagedToManagedTypes[type] = definition;
                SharedState.ManagedToUnmanagedTypes[definition] = type;
            }

            //Ensure we include all inner types within this type.
            foreach (var nested in type.NestedTypes!)
            {
                if (SharedState.TypeDefsByIndex.TryGetValue(nested.TypeIndex, out var alreadyMadeNestedType))
                {
                    //Type has already been defined (can be out-of-order in v27+) so we just add it.
                    definition.NestedTypes.Add(alreadyMadeNestedType);
                }
                else
                {
                    //Create it and register.
                    var nestedDef = new TypeDefinition(nested.Namespace, nested.Name, (TypeAttributes) nested.flags);

                    definition.NestedTypes.Add(nestedDef);
                    SharedState.AllTypeDefinitions.Add(nestedDef);
                    SharedState.TypeDefsByIndex[nested.TypeIndex] = nestedDef;
                    SharedState.UnmanagedToManagedTypes[nested] = nestedDef;
                    SharedState.ManagedToUnmanagedTypes[nestedDef] = nested;
                }
            }
        }
    }
}