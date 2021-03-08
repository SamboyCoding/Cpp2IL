using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL
{
    public static class LibCpp2IlMain
    {
        public class LibCpp2IlSettings
        {
            public bool AllowManualMetadataAndCodeRegInput;
            public bool DisableMethodPointerMapping;
            public bool DisableGlobalResolving;
        }

        public static readonly LibCpp2IlSettings Settings = new();

        public static float MetadataVersion = 24f;
        public static PE.PE? ThePe;
        public static Il2CppMetadata? TheMetadata;

        private static readonly Dictionary<ulong, List<Il2CppMethodDefinition>> MethodsByPtr = new();

        public static List<Il2CppMethodDefinition>? GetManagedMethodImplementationsAtAddress(ulong addr)
        {
            MethodsByPtr.TryGetValue(addr, out var ret);

            return ret;
        }

        public static MetadataUsage? GetAnyGlobalByAddress(ulong address)
        {
            if (MetadataVersion >= 27)
                return LibCpp2IlGlobalMapper.CheckForPost27GlobalAt(address);

            //Pre-27
            var glob = GetLiteralGlobalByAddress(address);
            glob ??= GetMethodGlobalByAddress(address);
            glob ??= GetRawFieldGlobalByAddress(address);
            glob ??= GetRawTypeGlobalByAddress(address);

            return glob;
        }

        public static MetadataUsage? GetLiteralGlobalByAddress(ulong address)
        {
            if (MetadataVersion < 27)
                return LibCpp2IlGlobalMapper.LiteralsByAddress.GetValueOrDefault(address);
            
            return GetAnyGlobalByAddress(address);
        }

        public static string? GetLiteralByAddress(ulong address)
        {
            var literal = GetLiteralGlobalByAddress(address);
            return literal?.AsLiteral();
        }

        public static MetadataUsage? GetRawTypeGlobalByAddress(ulong address)
        {
            if (MetadataVersion < 27)
                return LibCpp2IlGlobalMapper.TypeRefsByAddress.GetValueOrDefault(address);

            return GetAnyGlobalByAddress(address);
        }

        public static Il2CppTypeReflectionData? GetTypeGlobalByAddress(ulong address)
        {
            if (TheMetadata == null) return null;

            var typeGlobal = GetRawTypeGlobalByAddress(address);

            return typeGlobal?.AsType();
        }

        public static MetadataUsage? GetRawFieldGlobalByAddress(ulong address)
        {
            if (MetadataVersion < 27)
                return LibCpp2IlGlobalMapper.FieldRefsByAddress.GetValueOrDefault(address);
            return GetAnyGlobalByAddress(address);
        }

        public static Il2CppFieldDefinition? GetFieldGlobalByAddress(ulong address)
        {
            if (TheMetadata == null) return null;

            var typeGlobal = GetRawFieldGlobalByAddress(address);

            return typeGlobal?.AsField();
        }

        public static MetadataUsage? GetMethodGlobalByAddress(ulong address)
        {
            if (TheMetadata == null) return null;

            if (MetadataVersion < 27)
                return LibCpp2IlGlobalMapper.MethodRefsByAddress.GetValueOrDefault(address);

            return GetAnyGlobalByAddress(address);
        }

        public static Il2CppMethodDefinition? GetMethodDefinitionByGlobalAddress(ulong address)
        {
            var global = GetMethodGlobalByAddress(address);

            if (global?.Type == MetadataUsageType.MethodRef)
                return global.AsGenericMethodRef().baseMethod;

            return global?.AsMethod();
        }

        /// <summary>
        /// Initialize the metadata and PE from a pair of byte arrays.
        /// </summary>
        /// <param name="peBytes">The content of the GameAssembly.dll file.</param>
        /// <param name="metadataBytes">The content of the global-metadata.dat file</param>
        /// <param name="unityVersion">The unity version, split on periods, with the patch version (e.g. f1) stripped out. For example, [2018, 2, 0]</param>
        /// <returns>True if the initialize succeeded, else false</returns>
        /// <throws><see cref="System.FormatException"/> if the metadata is invalid (bad magic number, bad version), or if the PE is invalid (bad header signature, bad magic number)<br/></throws>
        /// <throws><see cref="System.NotSupportedException"/> if the PE file specifies it is neither for AMD64 or i386 architecture</throws>
        public static bool Initialize(byte[] peBytes, byte[] metadataBytes, int[] unityVersion)
        {
            TheMetadata = Il2CppMetadata.ReadFrom(metadataBytes, unityVersion);

            if (TheMetadata == null)
                return false;

            Console.WriteLine("Read Metadata ok.");

            ThePe = new PE.PE(new MemoryStream(peBytes, 0, peBytes.Length, false, true), TheMetadata.maxMetadataUsages);
            if (!ThePe.PlusSearch(TheMetadata.methodDefs.Count(x => x.methodIndex >= 0), TheMetadata.typeDefs.Length))
                return false;

            Console.WriteLine("Read PE Data ok.");

            if (!Settings.DisableGlobalResolving && MetadataVersion < 27)
            {
                var start = DateTime.Now;
                Console.Write("Mapping Globals...");
                LibCpp2IlGlobalMapper.MapGlobalIdentifiers(TheMetadata, ThePe);
                Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds}ms)");
            }

            if (!Settings.DisableMethodPointerMapping)
            {
                var start = DateTime.Now;
                Console.Write("Mapping pointers to Il2CppMethodDefinitions...");
                foreach (var (method, ptr) in TheMetadata.methodDefs.AsParallel().Select(method => (method, ptr: method.MethodPointer)))
                {
                    if (!MethodsByPtr.ContainsKey(ptr))
                        MethodsByPtr[ptr] = new List<Il2CppMethodDefinition>();

                    MethodsByPtr[ptr].Add(method);
                }

                Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds}ms)");
            }

            return true;
        }

        /// <summary>
        /// Initialize the metadata and PE from their respective file locations.
        /// </summary>
        /// <param name="pePath">The path to the GameAssembly.dll file</param>
        /// <param name="metadataPath">The path to the global-metadata.dat file</param>
        /// <param name="unityVersion">The unity version, split on periods, with the patch version (e.g. f1) stripped out. For example, [2018, 2, 0]</param>
        /// <returns>True if the initialize succeeded, else false</returns>
        /// <throws><see cref="System.FormatException"/> if the metadata is invalid (bad magic number, bad version), or if the PE is invalid (bad header signature, bad magic number)<br/></throws>
        /// <throws><see cref="System.NotSupportedException"/> if the PE file specifies it is neither for AMD64 or i386 architecture</throws>
        public static bool LoadFromFile(string pePath, string metadataPath, int[] unityVersion)
        {
            var metadataBytes = File.ReadAllBytes(metadataPath);
            var peBytes = File.ReadAllBytes(pePath);

            return Initialize(peBytes, metadataBytes, unityVersion);
        }
    }
}