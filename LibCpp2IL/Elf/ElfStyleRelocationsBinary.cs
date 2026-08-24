using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibCpp2IL.Logging;

namespace LibCpp2IL.Elf;

public abstract class ElfStyleRelocationsBinary(Stream input) : Il2CppBinary(input)
{
    protected readonly List<(ulong start, ulong end)> relocationBlocks = [];

    protected void ApplyRelocations(IDictionary<ElfDynamicType, ulong> dynamicEntries, ulong loadBias, ElfMachine machine)
    {
        // The android bionic linker only verifies the DT_SYMENT entry if it exists, so we just skip checking
        if (!dynamicEntries.TryGetValue(ElfDynamicType.DT_SYMTAB, out var symtabAddress))
            return;

        var symtabEntrySize = (ulong)(is32Bit
            ? ElfDynamicSymbol32.StructSize
            : ElfDynamicSymbol64.StructSize);

        if (dynamicEntries.TryGetValue(ElfDynamicType.DT_REL, out var relRva))
        {
            var size = dynamicEntries[ElfDynamicType.DT_RELSZ];
            ApplyRelocationsImpl(ParseRelSection(relRva, size));
            MarkRelocationRegion(relRva, size);
        }
        else if (dynamicEntries.TryGetValue(ElfDynamicType.DT_RELA, out var relaRva))
        {
            var size = dynamicEntries[ElfDynamicType.DT_RELASZ];
            ApplyRelocationsImpl(ParseRelaSection(relaRva, size));
            MarkRelocationRegion(relaRva, size);
        }

        if (dynamicEntries.TryGetValue(ElfDynamicType.DT_JMPREL, out var jmprelRva))
        {
            var size = dynamicEntries[ElfDynamicType.DT_PLTRELSZ];
            var type = (ElfDynamicType)dynamicEntries[ElfDynamicType.DT_PLTREL];
            ApplyRelocationsImpl(type == ElfDynamicType.DT_REL
                ? ParseRelSection(jmprelRva, size)
                : ParseRelaSection(jmprelRva, size));

            MarkRelocationRegion(jmprelRva, size);
        }

        if (dynamicEntries.TryGetValue(ElfDynamicType.DT_ANDROID_REL, out var androidRelRva))
        {
            var size = dynamicEntries[ElfDynamicType.DT_ANDROID_RELSZ];
            ApplyRelocationsImpl(ParsePackedSection(androidRelRva, size, false));
            MarkRelocationRegion(androidRelRva, size);
        }

        if (dynamicEntries.TryGetValue(ElfDynamicType.DT_ANDROID_RELA, out var androidRelaRva))
        {
            var size = dynamicEntries[ElfDynamicType.DT_ANDROID_RELASZ];
            ApplyRelocationsImpl(ParsePackedSection(androidRelaRva, size, true));
            MarkRelocationRegion(androidRelaRva, size);
        }

        if (dynamicEntries.TryGetValue(ElfDynamicType.DT_RELR, out var relrRva))
        {
            var size = dynamicEntries[ElfDynamicType.DT_RELRSZ];
            var entrySize = dynamicEntries[ElfDynamicType.DT_RELRENT];
            ApplyRelrRelocations(relrRva, size, entrySize);
            MarkRelocationRegion(relrRva, size);
        }

        if (dynamicEntries.TryGetValue(ElfDynamicType.DT_ANDROID_RELR, out var androidRelrRva))
        {
            var size = dynamicEntries[ElfDynamicType.DT_ANDROID_RELRSZ];
            var entrySize = dynamicEntries[ElfDynamicType.DT_ANDROID_RELRENT];
            ApplyRelrRelocations(androidRelrRva, size, entrySize);
            MarkRelocationRegion(androidRelrRva, size);
        }

        return;

        void MarkRelocationRegion(ulong start, ulong size)
        {
            relocationBlocks.Add((loadBias + start, loadBias + start + size));
        }

        void ApplyRelocationsImpl(ICollection<(ulong Offset, ulong Info, ulong? Addend)> relocations)
        {
            LibLogger.VerboseNewline($"Applying {relocations.Count} relocations");

            var symbolCache = new Dictionary<uint, IElfDynamicSymbol>();

            foreach (var relocation in relocations)
            {
                var relocationAddress = loadBias + relocation.Offset;

                ElfRelocationType type;
                uint symbolIndex;
                ulong addend;

                if (relocation.Addend.HasValue)
                {
                    addend = relocation.Addend.Value;
                }
                else
                {
                    addend = ReadPointerAtVirtualAddress(relocationAddress);
                }

                if (is32Bit)
                {
                    type = (ElfRelocationType)(relocation.Info & byte.MaxValue);
                    symbolIndex = (uint)(relocation.Info >> 8);
                }
                else
                {
                    type = (ElfRelocationType)(relocation.Info & uint.MaxValue);
                    symbolIndex = (uint)(relocation.Info >> 32);
                }

                if (!symbolCache.TryGetValue(symbolIndex, out var symbolEntry))
                {
                    var symtabEntryAddress = loadBias + symtabAddress + symbolIndex * symtabEntrySize;

                    symbolEntry = is32Bit
                        ? ReadReadableAtVirtualAddress<ElfDynamicSymbol32>(symtabEntryAddress)
                        : ReadReadableAtVirtualAddress<ElfDynamicSymbol64>(symtabEntryAddress);

                    symbolCache[symbolIndex] = symbolEntry;
                }

                // R_ARM_NONE / R_AARCH64_NONE need to be ignored.
                if ((machine == ElfMachine.EM_ARM && type == ElfRelocationType.R_ARM_NONE)
                    || (machine == ElfMachine.EM_AARCH64 && type == ElfRelocationType.R_AARCH64_NONE))
                {
                    continue;
                }

                var (value, handled) = machine switch
                {
                    ElfMachine.EM_386 => type switch
                    {
                        ElfRelocationType.R_386_32 => (symbolEntry.Value + addend, true),
                        ElfRelocationType.R_386_PC32 => (symbolEntry.Value + addend - relocation.Offset, true),
                        ElfRelocationType.R_386_GLOB_DAT => (symbolEntry.Value, true),
                        ElfRelocationType.R_386_JMP_SLOT => (symbolEntry.Value, true),
                        _ => (0uL, false)
                    },
                    ElfMachine.EM_X86_64 => type switch
                    {
                        ElfRelocationType.R_AMD64_64 => (symbolEntry.Value + addend, true),
                        ElfRelocationType.R_AMD64_RELATIVE => (addend, true),
                        _ => (0uL, false)
                    },
                    ElfMachine.EM_ARM => type switch
                    {
                        ElfRelocationType.R_ARM_ABS32 => (symbolEntry.Value + addend, true),
                        ElfRelocationType.R_ARM_REL32 => (symbolEntry.Value + relocation.Offset - addend, true),
                        ElfRelocationType.R_ARM_COPY => (symbolEntry.Value, true),
                        _ => (0uL, false)
                    },
                    ElfMachine.EM_AARCH64 => type switch
                    {
                        ElfRelocationType.R_AARCH64_ABS64 => (symbolEntry.Value + addend, true),
                        ElfRelocationType.R_AARCH64_PREL64 => (symbolEntry.Value + addend - relocation.Offset, true),
                        ElfRelocationType.R_AARCH64_GLOB_DAT => (symbolEntry.Value + addend, true),
                        ElfRelocationType.R_AARCH64_JUMP_SLOT => (symbolEntry.Value + addend, true),
                        ElfRelocationType.R_AARCH64_RELATIVE => (symbolEntry.Value + addend, true),
                        _ => (0uL, false)
                    },
                    _ => (0uL, false)
                };

                if (handled)
                {
                    value += loadBias;
                    WriteWord((int)MapVirtualAddressToRaw(relocationAddress), value);
                }
            }
        }

        List<(ulong, ulong, ulong?)> ParseRelSection(ulong rva, ulong size)
        {
            var entrySize = is32Bit ? ElfRelEntry.StructSize32Bit : ElfRelEntry.StructSize64Bit;
            var entryCount = size / (uint)entrySize;

            return ReadReadableArrayAtVirtualAddress<ElfRelEntry>(loadBias + rva, (int)entryCount)
                .Select<ElfRelEntry, (ulong, ulong, ulong?)>(x => (x.Offset, x.Info, null))
                .ToList();
        }

        List<(ulong, ulong, ulong?)> ParseRelaSection(ulong rva, ulong size)
        {
            var entrySize = is32Bit ? ElfRelaEntry.StructSize32Bit : ElfRelaEntry.StructSize64Bit;
            var entryCount = size / (uint)entrySize;

            return ReadReadableArrayAtVirtualAddress<ElfRelaEntry>(loadBias + rva, (int)entryCount)
                .Select<ElfRelaEntry, (ulong, ulong, ulong?)>(x => (x.Offset, x.Info, x.Addend))
                .ToList();
        }

        List<(ulong, ulong, ulong?)> ParsePackedSection(ulong rva, ulong size, bool isRela)
        {
            if (4 > size)
                return [];

            var off = loadBias + rva;
            Position = MapVirtualAddressToRaw(off);

            var magic = ReadBytes(4).AsSpan();
            if (!magic.SequenceEqual("APS2"u8))
                return [];

            var count = BaseStream.ReadLEB128Signed();
            var relocs = new List<(ulong, ulong, ulong?)>((int)count);

            var offset = BaseStream.ReadLEB128Signed();
            var info = 0L;
            var addend = 0L;

            for (var relocIndex = 0L; relocIndex < count; relocIndex++)
            {
                var groupSize = BaseStream.ReadLEB128Signed();
                var groupFlags = (ElfAndroidRelocationFlags)BaseStream.ReadLEB128Signed();

                var isGroupedByInfo = groupFlags.HasFlag(ElfAndroidRelocationFlags.RELOCATION_GROUPED_BY_INFO_FLAG);
                var isGroupedByOffsetDelta = groupFlags.HasFlag(ElfAndroidRelocationFlags.RELOCATION_GROUPED_BY_OFFSET_DELTA_FLAG);
                var isGroupedByAddend = groupFlags.HasFlag(ElfAndroidRelocationFlags.RELOCATION_GROUPED_BY_ADDEND_FLAG);
                var hasAddend = groupFlags.HasFlag(ElfAndroidRelocationFlags.RELOCATION_GROUP_HAS_ADDEND_FLAG);

                var groupRelativeOffsetDelta = 0L;
                if (isGroupedByOffsetDelta)
                    groupRelativeOffsetDelta = BaseStream.ReadLEB128Signed();

                if (isGroupedByInfo)
                    info = BaseStream.ReadLEB128Signed();

                if (isRela)
                {
                    if (hasAddend && isGroupedByAddend)
                    {
                        addend += BaseStream.ReadLEB128Signed();
                    }
                    else if (!hasAddend && !isGroupedByAddend)
                    {
                        addend = 0;
                    }
                }

                for (var i = 0L; i < groupSize; i++)
                {
                    if (isGroupedByOffsetDelta)
                    {
                        offset += groupRelativeOffsetDelta;
                    }
                    else
                    {
                        offset += BaseStream.ReadLEB128Signed();
                    }

                    if (!isGroupedByInfo)
                    {
                        info = BaseStream.ReadLEB128Signed();
                    }

                    if (isRela && !isGroupedByAddend && hasAddend)
                    {
                        addend += BaseStream.ReadLEB128Signed();
                    }

                    relocs.Add(((ulong)offset, (ulong)info, (ulong)addend));
                }

                relocIndex += groupSize;
            }

            return relocs;
        }

        void ApplyRelrRelocations(ulong rva, ulong size, ulong entrySize)
        {
            var pointerSize = (uint)(is32Bit ? sizeof(uint) : sizeof(ulong));
            var entryCount = size / pointerSize;

            var baseAddr = 0ul;
            for (ulong i = 0; i < entryCount; i++)
            {
                var word = ReadPointerAtVirtualAddress(loadBias + rva + i * pointerSize);
                ulong relocationAddress;

                if ((word & 1) == 0)
                {
                    relocationAddress = loadBias + word;

                    var relocationRawAddress = MapVirtualAddressToRaw(relocationAddress);
                    var value = ReadNUintAtRawAddress(relocationRawAddress);
                    WriteWord((int)relocationRawAddress, loadBias + value);

                    baseAddr = relocationAddress + entrySize;
                }
                else
                {
                    relocationAddress = baseAddr;
                    while (word != 0)
                    {
                        word >>= 1;

                        if ((word & 1) != 0)
                        {
                            var relocationRawAddress = MapVirtualAddressToRaw(relocationAddress);
                            var value = ReadNUintAtRawAddress(relocationRawAddress);
                            WriteWord((int)relocationRawAddress, loadBias + value);
                        }

                        relocationAddress += entrySize;
                    }

                    baseAddr += (8 * entrySize - 1) * entrySize;
                }
            }
        }
    }
}
