using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibCpp2IL.Elf;
using LibCpp2IL.Logging;
using LibCpp2IL.MachO;
using LibCpp2IL.Metadata;
using LibCpp2IL.NintendoSwitch;
using LibCpp2IL.Wasm;

namespace LibCpp2IL;

public static class LibCpp2IlBinaryRegistry
{
    private static List<RegisteredBinary> _binaries = [];

    public static void RegisterBuiltInBinarySupport()
    {
        Register("Portable Executable", "LibCpp2IL",
            bytes => BitConverter.ToInt16(bytes, 0) == 0x5A4D, //MZ
            (memStream) => new PE.PE(memStream));

        Register("ELF", "LibCpp2IL",
            bytes => BitConverter.ToInt32(bytes, 0) == 0x464c457f, //0x7F ELF
            (memStream) => new ElfFile(memStream));

        Register("Nintendo Switch Object", "LibCpp2IL",
            bytes => BitConverter.ToInt32(bytes, 0) == 0x304F534E, //NSO0
            (memStream) => new NsoFile(memStream).Decompress());

        Register("WebAssembly File", "LibCpp2IL",
            bytes => BitConverter.ToInt32(bytes, 0) == 0x6D736100, //\0WASM
            (memStream) => new WasmFile(memStream));

        Register("Mach-O File", "LibCppIL",
            bytes => BitConverter.ToUInt32(bytes, 0) is 0xFEEDFACE or 0xFEEDFACF,
            (memStream) => new MachOFile(memStream)
        );
    }

    public static void Register<T>(string name, string source, Func<byte[], bool> isValid, Func<MemoryStream, T> factory) where T : Il2CppBinary
    {
        _binaries.Add(new(name, source, isValid, factory));
    }

    internal static Il2CppBinary CreateAndInit(byte[] buffer, Il2CppMetadata metadata)
    {
        if (_binaries.Count == 0)
            RegisterBuiltInBinarySupport();

        var match = _binaries.Find(b => b.IsValid(buffer));

        if (match == null)
            throw new($"Unknown binary type, no binary handling header bytes {string.Join(" ", buffer.SubArray(0, 4).Select(b => $"{b:X2}"))} has been registered");

        LibLogger.InfoNewline($"Using binary type {match.Name} (from {match.Source})");

        var memStream = new MemoryStream(buffer, 0, buffer.Length, true, true);

        LibLogger.InfoNewline("Searching Binary for Required Data...");
        var start = DateTime.Now;

        var binary = match.FactoryFunc(memStream);

        LibCpp2IlMain.Binary = binary;

        var (codereg, metareg) = binary.FindCodeAndMetadataReg(metadata.methodDefs.Count(x => x.methodIndex >= 0), metadata.typeDefs.Length);

        LibLogger.InfoNewline($"Got Binary codereg: 0x{codereg:X}, metareg: 0x{metareg:X} in {(DateTime.Now - start).TotalMilliseconds:F0}ms.");
        LibLogger.InfoNewline("Initializing Binary...");
        start = DateTime.Now;

        binary.Init(codereg, metareg);

        LibLogger.InfoNewline($"Initialized Binary in {(DateTime.Now - start).TotalMilliseconds:F0}ms");

        return binary;
    }

    private class RegisteredBinary(
        string name,
        string source,
        Func<byte[], bool> verificationFunc,
        Func<MemoryStream, Il2CppBinary> factoryFunc)
    {
        public string Name = name;
        public string Source = source;
        public Func<byte[], bool> IsValid = verificationFunc;
        public Func<MemoryStream, Il2CppBinary> FactoryFunc = factoryFunc;
    }
}
