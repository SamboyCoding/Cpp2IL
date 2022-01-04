using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibCpp2IL.Elf;
using LibCpp2IL.Logging;
using LibCpp2IL.Metadata;
using LibCpp2IL.NintendoSwitch;
using LibCpp2IL.Wasm;

namespace LibCpp2IL
{
    public static class LibCpp2IlBinaryRegistry
    {
        private static List<RegisteredBinary> _binaries = new();

        public static void RegisterBuiltInBinarySupport()
        {
            Register("Portable Executable", 
                bytes => BitConverter.ToInt16(bytes, 0) == 0x5A4D, //MZ
                (memStream, maxUsages) => new PE.PE(memStream, maxUsages));
            
            Register("ELF",
                bytes => BitConverter.ToInt32(bytes, 0) == 0x464c457f, //0x7F ELF
                (memStream, maxUsages) => new ElfFile(memStream, maxUsages));
            
            Register("Nintendo Switch Object",
                bytes => BitConverter.ToInt32(bytes, 0) == 0x304F534E, //NSO0
                (memStream, maxUsages) => new NsoFile(memStream, maxUsages).Decompress());
            
            Register("WebAssembly File",
                bytes => BitConverter.ToInt32(bytes, 0) == 0x6D736100, //\0WASM
                (memStream, maxUsages) => new WasmFile(memStream, maxUsages));
        } 
        
        public static void Register<T>(string name, Func<byte[], bool> isValid, Func<MemoryStream, long, T> factory) where T : Il2CppBinary
        {
            _binaries.Add(new(name, isValid, factory));
        }

        internal static Il2CppBinary CreateAndInit(byte[] buffer, Il2CppMetadata metadata)
        {
            if(_binaries.Count == 0)
                RegisterBuiltInBinarySupport();
            
            var match = _binaries.Find(b => b.IsValid(buffer));
            
            if(match == null)
                throw new($"Unknown binary type, no binary handling header bytes {string.Join(" ", buffer.SubArray(0, 4).Select(b => $"{b:X2}"))} has been registered");

            LibLogger.InfoNewline($"Using binary type {match.Name}");
            
            var memStream = new MemoryStream(buffer, 0, buffer.Length, true, true);
            
            LibLogger.InfoNewline("Searching Binary for Required Data...");
            var start = DateTime.Now;
            
            var binary =  match.FactoryFunc(memStream, metadata.maxMetadataUsages);

            LibCpp2IlMain.Binary = binary;

            var (codereg, metareg) = binary.FindCodeAndMetadataReg(metadata.methodDefs.Count(x => x.methodIndex >= 0), metadata.typeDefs.Length);
            
            if (codereg == 0 || metareg == 0)
                throw new("Failed to find Binary code or metadata registration");
            
            LibLogger.InfoNewline($"Got Binary codereg: 0x{codereg:X}, metareg: 0x{metareg:X} in {(DateTime.Now - start).TotalMilliseconds:F0}ms.");
            LibLogger.InfoNewline("Initializing Binary...");
            start = DateTime.Now;
            
            binary.Init(codereg, metareg);
            
            LibLogger.InfoNewline($"Initialized Binary in {(DateTime.Now - start).TotalMilliseconds:F0}ms");

            return binary;
        }

        private class RegisteredBinary
        {
            public string Name;
            public Func<byte[], bool> IsValid;
            public Func<MemoryStream, long, Il2CppBinary> FactoryFunc;

            public RegisteredBinary(string name, Func<byte[], bool> verificationFunc, Func<MemoryStream, long, Il2CppBinary> factoryFunc)
            {
                Name = name;
                IsValid = verificationFunc;
                FactoryFunc = factoryFunc;
            }
        }
    }
}