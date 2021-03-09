using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Cpp2IL
{
    public class StubAssemblyBuilder
    {
        /// <summary>
        /// Creates all the Assemblies defined in the provided metadata, along with (stub) definitions of all the types contained therein.
        /// </summary>
        /// <param name="metadata">The Il2Cpp metadata to extract assemblies from</param>
        /// <param name="moduleParams">Configuration for the module creation.</param>
        /// <returns>A list of Mono.Cecil Assemblies, containing empty type definitions for each defined type.</returns>
        internal static List<AssemblyDefinition> BuildStubAssemblies(Il2CppMetadata metadata, ModuleParameters moduleParams)
        {
            return metadata.imageDefinitions
                .AsParallel()
                .Select(assemblyDefinition => BuildStubAssembly(moduleParams, assemblyDefinition))
                .ToList();
        }

        private static AssemblyDefinition BuildStubAssembly(ModuleParameters moduleParams, Il2CppImageDefinition assemblyDefinition)
        {
            //Get the name of the assembly (= the name of the DLL without the file extension)
            var assemblyNameString = assemblyDefinition.Name!.Replace(".dll", "");

            //Build a Mono.Cecil assembly name from this name
            var asmName = new AssemblyNameDefinition(assemblyNameString, new Version("0.0.0.0"));

            //Create an empty assembly and register it
            var assembly = AssemblyDefinition.CreateAssembly(asmName, assemblyDefinition.Name, moduleParams);

            //Ensure it really _is_ empty
            var mainModule = assembly.MainModule;
            mainModule.Types.Clear();
            
            //Populate types.
            foreach (var type in assemblyDefinition.Types!) 
                HandleTypeInAssembly(type, mainModule);

            return assembly;
        }

        private static void HandleTypeInAssembly(Il2CppTypeDefinition type, ModuleDefinition mainModule)
        {
            //Get the metadata type info, its namespace, and name.
            var ns = type.Namespace!;
            var name = type.Name!;

            TypeDefinition? definition = null;
            if (type.declaringTypeIndex != -1)
            {
                //This is a type declared within another (inner class/type)

                //Have we already declared this type due to handling its parent?
                SharedState.TypeDefsByIndex.TryGetValue(type.TypeIndex, out definition);
            }

            if (definition == null)
            {
                //This is a new type (including nested type with parent not defined yet) so ensure it's registered
                definition = new TypeDefinition(ns, name, (TypeAttributes) type.flags);
                if (ns == "System" && name == "String")
                {
                    typeof(TypeReference).GetField("etype", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(definition, (byte) 0x0e); //mark as string
                }

                mainModule.Types.Add(definition);
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