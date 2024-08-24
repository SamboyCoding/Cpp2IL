using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibCpp2IL.Logging;
using LibCpp2IL.Metadata;
using WasmDisassembler;

namespace LibCpp2IL.Wasm;

public sealed class WasmFile : Il2CppBinary
{
    public static Dictionary<string, string>? RemappedDynCallFunctions;

    public readonly List<WasmFunctionDefinition> FunctionTable = [];

    internal readonly List<WasmSection> Sections = [];

    private byte[] _raw;
    private WasmMemoryBlock _memoryBlock;
    private readonly Dictionary<string, WasmDynCallCoefficients> DynCallCoefficients = new();

    public override ClassReadingBinaryReader Reader => _memoryBlock;

    public WasmFile(MemoryStream input) : base(input)
    {
        is32Bit = true;
        InstructionSetId = DefaultInstructionSets.WASM;
        _raw = input.GetBuffer();
        var magic = ReadUInt32();
        var version = ReadInt32();

        if (magic != 0x6D736100) //\0asm
            throw new Exception($"WASM magic mismatch; got 0x{magic:X}");

        if (version != 1)
            throw new Exception($"Unknown version, expecting 1, got {version}");

        LibLogger.VerboseNewline("\tWASM Magic and version match expectations. Reading sections...");

        var sanityCount = 0;
        while (Position < _raw.Length && sanityCount < 1000)
        {
            var section = WasmSection.MakeSection(this);
            Position = section.Pointer + (long)section.Size;
            Sections.Add(section);
            sanityCount++;
        }

        LibLogger.VerboseNewline($"\tRead {Sections.Count} WASM sections. Allocating memory block...");

        _memoryBlock = new(this);

        LibLogger.VerboseNewline($"\tAllocated memory block of {_memoryBlock.Bytes.Length} (0x{_memoryBlock.Bytes.Length:X}) bytes ({_memoryBlock.Bytes.Length / 1024f / 1024f:F2}MB). Constructing function table...");

        foreach (var importSectionEntry in ImportSection.Entries)
        {
            if (importSectionEntry.Kind == WasmExternalKind.EXT_FUNCTION)
                FunctionTable.Add(new(importSectionEntry));
        }

        for (var index = 0; index < CodeSection.Functions.Count; index++)
        {
            var codeSectionFunction = CodeSection.Functions[index];
            var functionTableIndex = FunctionTable.Count;
            FunctionTable.Add(new(this, codeSectionFunction, index, functionTableIndex));
        }

        LibLogger.VerboseNewline($"\tBuilt function table of {FunctionTable.Count} entries. Calculating dynCall coefficients...");

        CalculateDynCallOffsets();

        LibLogger.VerboseNewline($"\tGot dynCall coefficients for {DynCallCoefficients.Count} signatures");
    }

    public override long RawLength => _memoryBlock.Bytes.Length;
    public override byte GetByteAtRawAddress(ulong addr) => _memoryBlock.Bytes[addr];
    public override byte[] GetRawBinaryContent() => _memoryBlock.Bytes;

    public WasmFunctionDefinition GetFunctionFromIndexAndSignature(ulong index, string signature)
    {
        if (!DynCallCoefficients.TryGetValue(signature, out var coefficients))
            throw new($"Can't get function with signature {signature}, as it's not defined in the binary");

        //Calculate adjusted dyncall index
        index = (index & coefficients.andWith) + coefficients.addConstant;

        //Use element section to look up real index
        var realIndex = ElementSection.Elements[0].FunctionIndices![(int)index - 1]; //Minus 1 because the first element in the actual memory layout is FFFFFFFF

        //Look up real index in function table
        return FunctionTable[(int)realIndex];
    }

    internal WasmGlobalType[] GlobalTypes => ImportSection.Entries.Where(e => e.Kind == WasmExternalKind.EXT_GLOBAL).Select(e => e.GlobalEntry!).Concat(GlobalSection.Globals.Select(g => g.Type)).ToArray();

    internal WasmGlobalSection GlobalSection => (WasmGlobalSection)Sections.First(s => s.Type == WasmSectionId.SEC_GLOBAL);
    internal WasmTypeSection TypeSection => (WasmTypeSection)Sections.First(s => s.Type == WasmSectionId.SEC_TYPE);

    internal WasmFunctionSection FunctionSection => (WasmFunctionSection)Sections.First(s => s.Type == WasmSectionId.SEC_FUNCTION);

    internal WasmDataSection DataSection => (WasmDataSection)Sections.First(s => s.Type == WasmSectionId.SEC_DATA);
    internal WasmCodeSection CodeSection => (WasmCodeSection)Sections.First(s => s.Type == WasmSectionId.SEC_CODE);
    internal WasmImportSection ImportSection => (WasmImportSection)Sections.First(s => s.Type == WasmSectionId.SEC_IMPORT);
    internal WasmElementSection ElementSection => (WasmElementSection)Sections.First(s => s.Type == WasmSectionId.SEC_ELEMENT);
    internal WasmExportSection ExportSection => (WasmExportSection)Sections.First(s => s.Type == WasmSectionId.SEC_EXPORT);

    private void CalculateDynCallOffsets()
    {
        //Remap any exported functions we have remaps for

        if (RemappedDynCallFunctions != null)
        {
            foreach (var exportSectionExport in ExportSection.Exports.Where(e => e.Kind == WasmExternalKind.EXT_FUNCTION))
            {
                if (!RemappedDynCallFunctions.TryGetValue(exportSectionExport.Name, out var remappedName))
                    continue;

                LibLogger.VerboseNewline($"\t\tRemapped exported function {exportSectionExport.Name} to {remappedName}");
                exportSectionExport.Name.Value = remappedName;
            }
        }

        foreach (var exportedDynCall in ExportSection.Exports.Where(e => e.Kind == WasmExternalKind.EXT_FUNCTION && e.Name.Value.StartsWith("dynCall_")))
        {
            var signature = exportedDynCall.Name.Value["dynCall_".Length..];

            var function = FunctionTable[(int)exportedDynCall.Index].AssociatedFunctionBody;
            var funcBody = function!.Instructions;
            var disassembled = Disassembler.Disassemble(funcBody, (uint)function.InstructionsOffset);

            //Find the consts, ands, and adds
            var relevantInstructions = disassembled.Where(i => i.Mnemonic is WasmMnemonic.I32Const or WasmMnemonic.I32And or WasmMnemonic.I32Add).ToArray();

            ulong andWith;
            ulong add;

            if (relevantInstructions.Length == 2)
            {
                if (relevantInstructions[^1].Mnemonic == WasmMnemonic.I32And)
                {
                    andWith = (ulong)relevantInstructions[0].Operands[0];
                    add = 0;
                }
                else if (relevantInstructions[^1].Mnemonic == WasmMnemonic.I32Add)
                {
                    add = (ulong)relevantInstructions[0].Operands[0];
                    andWith = int.MaxValue;
                }
                else
                {
                    LibLogger.WarnNewline($"\t\tCouldn't calculate coefficients for {signature}, got only 2 instructions but the last was {relevantInstructions[^1].Mnemonic}, not I32And or I32Add");
                    continue;
                }
            }
            else if (relevantInstructions.Length == 4)
            {
                //Should be const, and, const, add
                if (!relevantInstructions.Select(i => i.Mnemonic).SequenceEqual(new[] { WasmMnemonic.I32Const, WasmMnemonic.I32And, WasmMnemonic.I32Const, WasmMnemonic.I32Add }))
                {
                    LibLogger.WarnNewline($"\t\tCouldn't calculate coefficients for {signature}, got mnemonics {string.Join(", ", relevantInstructions.Select(i => i.Mnemonic))}, expecting I32Const, I32And, I32Const, I32Add");
                    continue;
                }

                andWith = (ulong)relevantInstructions[0].Operands[0];
                add = (ulong)relevantInstructions[2].Operands[0];
            }
            else if (disassembled.All(d => d.Mnemonic is WasmMnemonic.LocalGet or WasmMnemonic.CallIndirect or WasmMnemonic.End))
            {
                //No remapping
                andWith = int.MaxValue;
                add = 0;
            }
            else if (disassembled[^1].Mnemonic == WasmMnemonic.End && disassembled[^2].Mnemonic == WasmMnemonic.CallIndirect && disassembled[^3].Mnemonic == WasmMnemonic.LocalGet && (byte)disassembled[^3].Operands[0] == 0)
            {
                //If we're ending with LocalGet 0, CallIndirect, End, then we're *probably* just keeping the same index as was passed in
                //Tentatively assume we're doing shenanigans only to the params and we don't touch the index
                LibLogger.VerboseNewline($"\t\tAssuming index is not manipulated for dynCall_{signature} (method ends with LocalGet 0, CallIndirect, End)");
                andWith = int.MaxValue;
                add = 0;
            }
            else
            {
                LibLogger.WarnNewline($"\t\tCouldn't calculate coefficients for {signature}, got {relevantInstructions.Length} instructions; expecting 4");
                continue;
            }

            DynCallCoefficients[signature] = new() { andWith = andWith, addConstant = add, };
        }
    }

    public override long MapVirtualAddressToRaw(ulong uiAddr, bool throwOnError = true)
    {
        // var data = DataSection;
        // var matchingEntry = data.DataEntries.FirstOrDefault(e => e.VirtualOffset <= uiAddr && e.VirtualOffset + (ulong) e.Data.Length > uiAddr);
        // if (matchingEntry != null)
        // {
        //     return matchingEntry.FileOffset + (long) (uiAddr - (ulong) matchingEntry.VirtualOffset);
        // }

        if (uiAddr > (ulong)(_memoryBlock.Bytes.Length + _raw.Length))
            if (throwOnError)
                throw new($"Way out of bounds! Requested 0x{uiAddr:X}, memory block + raw length = 0x{_memoryBlock.Bytes.Length + _raw.Length:X}");
            else
                return VirtToRawInvalidNoMatch;

        return (long)uiAddr;
    }

    public override ulong MapRawAddressToVirtual(uint offset)
    {
        var data = DataSection;
        if (offset > data.Pointer && offset < data.Pointer + (long)data.Size)
        {
            var segment = data.DataEntries.FirstOrDefault(entry => offset >= entry.FileOffset && offset < entry.FileOffset + entry.Data.Length);

            if (segment is { VirtualOffset: < ulong.MaxValue })
            {
                return segment.VirtualOffset + (ulong)(offset - segment.FileOffset);
            }
        }

        return offset;
    }

    public override Stream BaseStream => _memoryBlock?.BaseStream ?? base.BaseStream;

    //Delegate to the memory block
    internal override object? ReadPrimitive(Type type, bool overrideArchCheck = false) => _memoryBlock.ReadPrimitive(type, overrideArchCheck);

    public override string ReadStringToNull(long offset) => _memoryBlock.ReadStringToNull(offset);

    public override ulong GetRva(ulong pointer)
    {
        return pointer;
    }

    public override ulong GetVirtualAddressOfExportedFunctionByName(string toFind)
    {
        return 0; //Never going to be anything useful, so don't bother looking
    }

    public override byte[] GetEntirePrimaryExecutableSection() => ((WasmCodeSection)Sections.First(s => s.Type == WasmSectionId.SEC_CODE)).RawSectionContent;

    public override ulong GetVirtualAddressOfPrimaryExecutableSection() => (ulong)Sections.First(s => s.Type == WasmSectionId.SEC_CODE).Pointer;
}
