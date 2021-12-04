using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibCpp2IL.Elf;
using LibCpp2IL.Logging;
using LibCpp2IL.Metadata;
using LibCpp2IL.NintendoSwitch;
using LibCpp2IL.Reflection;
using LibCpp2IL.Wasm;

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
        public static Il2CppBinary? Binary;
        public static Il2CppMetadata? TheMetadata;

        public static readonly Dictionary<ulong, List<Il2CppMethodDefinition>> MethodsByPtr = new();

        public static void Reset()
        {
            LibCpp2IlGlobalMapper.Reset();
            LibCpp2ILUtils.Reset();
            MethodsByPtr.Clear();
        }

        public static List<Il2CppMethodDefinition>? GetManagedMethodImplementationsAtAddress(ulong addr)
        {
            MethodsByPtr.TryGetValue(addr, out var ret);

            return ret;
        }

        public static MetadataUsage? GetAnyGlobalByAddress(ulong address)
        {
            if (MetadataVersion >= 27f)
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
            if (MetadataVersion < 27f)
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
            if (MetadataVersion < 27f)
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
            if (MetadataVersion < 27f)
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

            if (MetadataVersion < 27f)
                return LibCpp2IlGlobalMapper.MethodRefsByAddress.GetValueOrDefault(address);

            return GetAnyGlobalByAddress(address);
        }

        public static Il2CppMethodDefinition? GetMethodDefinitionByGlobalAddress(ulong address)
        {
            var global = GetMethodGlobalByAddress(address);

            if (global?.Type == MetadataUsageType.MethodRef)
                return global.AsGenericMethodRef().BaseMethod;

            return global?.AsMethod();
        }

        /// <summary>
        /// Initialize the metadata and PE from a pair of byte arrays.
        /// </summary>
        /// <param name="binaryBytes">The content of the GameAssembly.dll file.</param>
        /// <param name="metadataBytes">The content of the global-metadata.dat file</param>
        /// <param name="unityVersion">The unity version, split on periods, with the patch version (e.g. f1) stripped out. For example, [2018, 2, 0]</param>
        /// <returns>True if the initialize succeeded, else false</returns>
        /// <throws><see cref="System.FormatException"/> if the metadata is invalid (bad magic number, bad version), or if the PE is invalid (bad header signature, bad magic number)<br/></throws>
        /// <throws><see cref="System.NotSupportedException"/> if the PE file specifies it is neither for AMD64 or i386 architecture</throws>
        public static bool Initialize(byte[] binaryBytes, byte[] metadataBytes, int[] unityVersion)
        {
            LibCpp2IlReflection.ResetCaches();
            
            var start = DateTime.Now;
            
            LibLogger.InfoNewline("Initializing Metadata...");
            
            TheMetadata = Il2CppMetadata.ReadFrom(metadataBytes, unityVersion);
            
            LibLogger.InfoNewline($"Initialized Metadata in {(DateTime.Now - start).TotalMilliseconds:F0}ms");

            if (TheMetadata == null)
                return false;

            LibLogger.InfoNewline("Searching Binary for Required Data...");
            start = DateTime.Now;

            ulong codereg, metareg;
            if (BitConverter.ToInt16(binaryBytes, 0) == 0x5A4D)
            {
                var pe = new PE.PE(new MemoryStream(binaryBytes, 0, binaryBytes.Length, false, true), TheMetadata.maxMetadataUsages);
                Binary = pe;

                (codereg, metareg) = pe.PlusSearch(TheMetadata.methodDefs.Count(x => x.methodIndex >= 0), TheMetadata.typeDefs.Length);
            } else if (BitConverter.ToInt32(binaryBytes, 0) == 0x464c457f)
            {
                var elf = new ElfFile(new MemoryStream(binaryBytes, 0, binaryBytes.Length, true, true), TheMetadata.maxMetadataUsages);
                Binary = elf;
                (codereg, metareg) = elf.FindCodeAndMetadataReg();
            }
            else if (BitConverter.ToInt32(binaryBytes, 0) == 0x304F534E) //NSO0
            {
                var nso = new NsoFile(new MemoryStream(binaryBytes, 0, binaryBytes.Length, false, true), TheMetadata.maxMetadataUsages);
                nso = nso.Decompress();
                Binary = nso;
                (codereg, metareg) = nso.PlusSearch(TheMetadata.methodDefs.Count(x => x.methodIndex >= 0), TheMetadata.typeDefs.Length);
            } else if (BitConverter.ToInt32(binaryBytes, 0) == 0x6D736100) //\0WASM
            {
                var wasm = new WasmFile(new MemoryStream(binaryBytes, 0, binaryBytes.Length, false, true), TheMetadata.maxMetadataUsages);
                Binary = wasm;
                (codereg, metareg) = wasm.PlusSearch(TheMetadata.methodDefs.Count(x => x.methodIndex >= 0), TheMetadata.typeDefs.Length);
            }
            else
            {
                throw new Exception("Unknown binary type");
            }
            
            if (codereg == 0 || metareg == 0)
                throw new Exception("Failed to find Binary code or metadata registration");
                
            LibLogger.InfoNewline($"Got Binary codereg: 0x{codereg:X}, metareg: 0x{metareg:X} in {(DateTime.Now - start).TotalMilliseconds:F0}ms.");
            LibLogger.InfoNewline("Initializing Binary...");
            start = DateTime.Now;
                
            Binary.Init(codereg, metareg);
            
            LibLogger.InfoNewline($"Initialized Binary in {(DateTime.Now - start).TotalMilliseconds:F0}ms");

            if (!Settings.DisableGlobalResolving && MetadataVersion < 27)
            {
                start = DateTime.Now;
                LibLogger.Info("Mapping Globals...");
                LibCpp2IlGlobalMapper.MapGlobalIdentifiers(TheMetadata, Binary);
                LibLogger.InfoNewline($"OK ({(DateTime.Now - start).TotalMilliseconds:F0}ms)");
            }

            if (!Settings.DisableMethodPointerMapping)
            {
                start = DateTime.Now;
                LibLogger.Info("Mapping pointers to Il2CppMethodDefinitions...");
                var i = 0;
                foreach (var (method, ptr) in TheMetadata.methodDefs.Select(method => (method, ptr: method.MethodPointer)))
                {
                    if (!MethodsByPtr.ContainsKey(ptr))
                        MethodsByPtr[ptr] = new List<Il2CppMethodDefinition>();

                    MethodsByPtr[ptr].Add(method);
                    i++;
                }

                LibLogger.InfoNewline($"Processed {i} OK ({(DateTime.Now - start).TotalMilliseconds:F0}ms)");
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