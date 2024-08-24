using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Logging;
using LibCpp2IL.NintendoSwitch;
using LibCpp2IL.Wasm;

namespace LibCpp2IL;

public class BinarySearcher(Il2CppBinary binary, int methodCount, int typeDefinitionsCount)
{
    private readonly byte[] _binaryBytes = binary.GetRawBinaryContent();

    //Used for codereg location pre-2019
    //Used for metadata reg location in 24.5+

    private static int FindSequence(byte[] haystack, byte[] needle, int requiredAlignment = 1, int startOffset = 0)
    {
        //Convert needle to a span now, rather than in the loop (implicitly as call to SequenceEqual)
        var needleSpan = new ReadOnlySpan<byte>(needle);
        var haystackSpan = haystack.AsSpan();
        var firstByte = needleSpan[0];

        //Find the first occurrence of the first byte of the needle
        var nextMatchIdx = Array.IndexOf(haystack, firstByte, startOffset);

        var needleLength = needleSpan.Length;
        var endIdx = haystack.Length - needleLength;
        var checkAlignment = requiredAlignment > 1;

        while (0 <= nextMatchIdx && nextMatchIdx <= endIdx)
        {
            //If we're not aligned, skip this match
            if (!checkAlignment || nextMatchIdx % requiredAlignment == 0)
            {
                //Take a slice of the array at this position and the length of the needle, and compare
                if (haystackSpan.Slice(nextMatchIdx, needleLength).SequenceEqual(needleSpan))
                    return nextMatchIdx;
            }

            //Find the next occurrence of the first byte of the needle
            nextMatchIdx = Array.IndexOf(haystack, firstByte, nextMatchIdx + 1);
        }

        //No match found
        return -1;
    }

    // Find all occurrences of a sequence of bytes, using word alignment by default
    private IEnumerable<uint> FindAllBytes(byte[] signature, int alignment = 0)
    {
        LibLogger.VerboseNewline($"\t\t\tLooking for bytes: {string.Join(" ", signature.Select(b => b.ToString("x2")))}");
        var offset = 0;
        var ptrSize = binary.is32Bit ? 4 : 8;
        while (offset != -1)
        {
            offset = FindSequence(_binaryBytes, signature, alignment != 0 ? alignment : ptrSize, offset);
            if (offset != -1)
            {
                yield return (uint)offset;
                offset += ptrSize;
            }
        }
    }

    // Find strings
    public IEnumerable<uint> FindAllStrings(string str) => FindAllBytes(Encoding.ASCII.GetBytes(str), 1);

    // Find 32-bit words
    private IEnumerable<uint> FindAllDWords(uint word) => FindAllBytes(BitConverter.GetBytes(word), binary is NsoFile ? 1 : 4);

    // Find 64-bit words
    private IEnumerable<uint> FindAllQWords(ulong word) => FindAllBytes(BitConverter.GetBytes(word), binary is NsoFile ? 1 : 8);

    // Find words for the current binary size
    private IEnumerable<uint> FindAllWords(ulong word)
        => binary.is32Bit ? FindAllDWords((uint)word) : FindAllQWords(word);

    private IEnumerable<ulong> MapOffsetsToVirt(IEnumerable<uint> offsets)
    {
        foreach (var offset in offsets)
            if (binary.TryMapRawAddressToVirtual(offset, out var word))
                yield return word;
    }

    // Find all valid virtual address pointers to a virtual address
    public IEnumerable<ulong> FindAllMappedWords(ulong word)
    {
        var fileOffsets = FindAllWords(word).ToList();
        return MapOffsetsToVirt(fileOffsets);
    }

    // Find all valid virtual address pointers to a set of virtual addresses
    public IEnumerable<ulong> FindAllMappedWords(IEnumerable<ulong> va) => va.SelectMany(FindAllMappedWords);

    public IEnumerable<ulong> FindAllMappedWords(IEnumerable<uint> va) => va.SelectMany(a => FindAllMappedWords(a));

    public ulong FindCodeRegistrationPre2019()
    {
        //First item in the CodeRegistration is the number of methods.
        var vas = MapOffsetsToVirt(FindAllBytes(BitConverter.GetBytes(methodCount), 1)).ToList();

        LibLogger.VerboseNewline($"\t\t\tFound {vas.Count} instances of the method count {methodCount}, as bytes {string.Join(", ", BitConverter.GetBytes((ulong)methodCount).Select(x => $"0x{x:X}"))}");

        if (vas.Count == 0)
            return 0;

        foreach (var va in vas)
        {
            LibLogger.VerboseNewline($"\t\t\tChecking for CodeRegistration at virtual address 0x{va:x}...");
            var cr = binary.ReadReadableAtVirtualAddress<Il2CppCodeRegistration>(va);

            if ((long)cr.customAttributeCount == LibCpp2IlMain.TheMetadata!.attributeTypeRanges.Count)
                return va;

            LibLogger.VerboseNewline($"\t\t\t\tNot a valid CodeRegistration - custom attribute count is {cr.customAttributeCount}, expecting {LibCpp2IlMain.TheMetadata!.attributeTypeRanges.Count}");
        }

        return 0;
    }

    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    internal ulong FindCodeRegistrationPost2019()
    {
        //Works only on >=24.2
        var mscorlibs = FindAllStrings("mscorlib.dll\0").Select(idx => binary.MapRawAddressToVirtual(idx)).ToList();

        LibLogger.VerboseNewline($"\t\t\tFound {mscorlibs.Count} occurrences of mscorlib.dll: [{string.Join(", ", mscorlibs.Select(p => p.ToString("X")))}]");

        var pMscorlibCodegenModule = FindAllMappedWords(mscorlibs).ToList(); //CodeGenModule address will be in here

        LibLogger.VerboseNewline($"\t\t\tFound {pMscorlibCodegenModule.Count} potential codegen modules for mscorlib: [{string.Join(", ", pMscorlibCodegenModule.Select(p => p.ToString("X")))}]");

        var pMscorlibCodegenEntryInCodegenModulesList = FindAllMappedWords(pMscorlibCodegenModule).ToList(); //CodeGenModules list address will be in here

        LibLogger.VerboseNewline($"\t\t\tFound {pMscorlibCodegenEntryInCodegenModulesList.Count} address for potential codegen modules in potential codegen module lists: [{string.Join(", ", pMscorlibCodegenEntryInCodegenModulesList.Select(p => p.ToString("X")))}]");

        if (pMscorlibCodegenEntryInCodegenModulesList.Count == 0)
        {
            LibLogger.ErrorNewline("\t\t\tNo codegen modules found for mscorlib! Aborting search.");
            return 0;
        }

        var ptrSize = (binary.is32Bit ? 4u : 8u);

        List<ulong>? pCodegenModules = null;
        if (LibCpp2IlMain.MetadataVersion < 27f)
        {
            //Pre-v27, mscorlib is the first codegen module, so *MscorlibCodegenEntryInCodegenModulesList == g_CodegenModules, so we can just find a pointer to this.
            if (pMscorlibCodegenEntryInCodegenModulesList.Count == 1)
                //Small optimisation in the case of only one ptr
                pCodegenModules = FindAllMappedWords(pMscorlibCodegenEntryInCodegenModulesList[0]).ToList();
            else
                pCodegenModules = FindAllMappedWords(pMscorlibCodegenEntryInCodegenModulesList).ToList();
        }
        else
        {
            //but in v27 it's close to the LAST codegen module (winrt.dll is an exception), so we need to work back until we find an xref.
            var sanityCheckNumberOfModules = Math.Min(400, LibCpp2IlMain.TheMetadata!.imageDefinitions.Length);
            var pSomewhereInCodegenModules = pMscorlibCodegenEntryInCodegenModulesList.AsEnumerable();
            var numModuleDefs = LibCpp2IlMain.TheMetadata!.imageDefinitions.Length;
            var initialBacktrack = numModuleDefs - 10;

            pSomewhereInCodegenModules = pSomewhereInCodegenModules.Select(va => va - ptrSize * (ulong)initialBacktrack);

            //Slightly experimental, but we're gonna try backtracking most of the way through the number of modules. Not all the way because we don't want to overshoot.
            int backtrack;
            for (backtrack = initialBacktrack; backtrack < sanityCheckNumberOfModules && (pCodegenModules?.Count() ?? 0) != 1; backtrack++)
            {
                pCodegenModules = FindAllMappedWords(pSomewhereInCodegenModules).ToList();

                //Sanity check the count, which is one pointer back
                if (pCodegenModules.Count == 1)
                {
                    binary.Reader.Position = binary.MapVirtualAddressToRaw(pCodegenModules.First() - ptrSize);
                    var moduleCount = binary.Reader.ReadInt32();

                    if (moduleCount < 0 || moduleCount > sanityCheckNumberOfModules)
                        pCodegenModules = [];
                    else
                        LibLogger.VerboseNewline($"\t\t\tFound valid address for pCodegenModules after a backtrack of {backtrack}, module count is {LibCpp2IlMain.TheMetadata!.imageDefinitions.Length}");
                }
                else if (pCodegenModules.Count > 1)
                {
                    LibLogger.VerboseNewline($"\t\t\tFound {pCodegenModules.Count} potential pCodegenModules addresses after a backtrack of {backtrack}, which is too many (> 1). Will try backtracking further.");
                }

                pSomewhereInCodegenModules = pSomewhereInCodegenModules.Select(va => va - ptrSize);
            }

            if (backtrack == sanityCheckNumberOfModules && (pCodegenModules?.Count() ?? 0) != 1)
            {
                LibLogger.WarnNewline($"Hit backtrack limit of {backtrack} modules and still didn't find a valid pCodegenModules pointer.");
                return 0;
            }

            if (pCodegenModules?.Any() != true)
                throw new Exception("Failed to find pCodegenModules");

            if (pCodegenModules.Count() > 1)
                throw new Exception("Found more than 1 pointer as pCodegenModules");
        }

        LibLogger.VerboseNewline($"\t\t\tFound {pCodegenModules.Count} potential pCodegenModules addresses: [{string.Join(", ", pCodegenModules.Select(p => p.ToString("X")))}]");

        //We have pCodegenModules which *should* be x-reffed in the last pointer of Il2CppCodeRegistration.
        //So, subtract the size of one pointer from that...
        var bytesToGoBack = (ulong)Il2CppCodeRegistration.GetStructSize(binary.is32Bit, LibCpp2IlMain.MetadataVersion) - ptrSize;

        LibLogger.VerboseNewline($"\t\t\tpCodegenModules is the second-to-last field of the codereg struct. Therefore on this version and architecture, we need to subtract {bytesToGoBack} bytes from its address to get pCodeReg");

        var fields = typeof(Il2CppCodeRegistration).GetFields();
        var fieldsByName = fields.ToDictionary(f => f.Name);

        foreach (var pCodegenModule in pCodegenModules)
        {
            //...and subtract that from our pointer.
            var address = pCodegenModule - bytesToGoBack;

            if (pCodegenModules.Count == 1)
            {
                LibLogger.VerboseNewline($"\t\t\tOnly found one codegen module pointer, so assuming it's correct and returning pCodeReg = 0x{address:X}");
                return address;
            }

            LibLogger.Verbose($"\t\t\tConsidering potential code registration at 0x{address:X}...");

            var codeReg = LibCpp2IlMain.Binary!.ReadReadableAtVirtualAddress<Il2CppCodeRegistration>(address);

            var success = ValidateCodeRegistration(codeReg, fieldsByName);

            if (success)
            {
                LibLogger.VerboseNewline("Looks good!");
                return address;
            }
        }

        return 0;
    }

    public static bool ValidateCodeRegistration(Il2CppCodeRegistration codeReg, Dictionary<string, FieldInfo> fieldsByName)
    {
        var success = true;
        foreach (var keyValuePair in fieldsByName)
        {
            var fieldValue = (ulong)keyValuePair.Value.GetValue(codeReg)!;

            if (fieldValue == 0)
                continue; //Allow zeroes

            if (keyValuePair.Key.EndsWith("count", StringComparison.OrdinalIgnoreCase))
            {
                //19 Apr 2022: Upped to 0x70000 due to Zenith (which has genericMethodPointersCount = 0x6007B)
                if (fieldValue > 0x70_000)
                {
                    LibLogger.VerboseNewline($"Rejected due to unreasonable count field 0x{fieldValue:X} for field {keyValuePair.Key}");
                    success = false;
                    break;
                }
            }
            else
            {
                //Pointer
                if (!LibCpp2IlMain.Binary!.TryMapVirtualAddressToRaw(fieldValue, out _))
                {
                    LibLogger.VerboseNewline($"Rejected due to invalid pointer 0x{fieldValue:X} for field {keyValuePair.Key}");
                    success = false;
                    break;
                }
            }
        }

        return success;
    }

    public ulong FindMetadataRegistrationPre24_5()
    {
        //We're looking for TypeDefinitionsSizesCount, which is the 4th-to-last field
        var sizeOfMr = (ulong)Il2CppMetadataRegistration.GetStructSize(binary.is32Bit);
        var ptrSize = binary.is32Bit ? 4ul : 8ul;

        var bytesToSubtract = sizeOfMr - ptrSize * 4;

        var potentialMetaRegPointers = MapOffsetsToVirt(FindAllBytes(BitConverter.GetBytes(LibCpp2IlMain.TheMetadata!.typeDefs.Length), 1)).ToList();

        LibLogger.VerboseNewline($"\t\t\tFound {potentialMetaRegPointers.Count} instances of the number of type defs, {LibCpp2IlMain.TheMetadata.typeDefs.Length}");

        potentialMetaRegPointers = potentialMetaRegPointers.Select(p => p - bytesToSubtract).ToList();

        foreach (var potentialMetaRegPointer in potentialMetaRegPointers)
        {
            var mr = binary.ReadReadableAtVirtualAddress<Il2CppMetadataRegistration>(potentialMetaRegPointer);

            if (mr.metadataUsagesCount == (ulong)LibCpp2IlMain.TheMetadata!.metadataUsageLists.Length)
            {
                LibLogger.VerboseNewline($"\t\t\tFound and selected probably valid metadata registration at 0x{potentialMetaRegPointer:X}.");
                return potentialMetaRegPointer;
            }
            else
                LibLogger.VerboseNewline($"\t\t\tSkipping 0x{potentialMetaRegPointer:X} as the metadata reg, metadata usage count was 0x{mr.metadataUsagesCount:X}, expecting 0x{LibCpp2IlMain.TheMetadata.metadataUsageLists.Length:X}");
        }

        return 0;
    }

    public ulong FindMetadataRegistrationPost24_5()
    {
        var ptrSize = binary.is32Bit ? 4ul : 8ul;
        var sizeOfMr = (uint)Il2CppMetadataRegistration.GetStructSize(binary.is32Bit);

        LibLogger.VerboseNewline($"\t\t\tLooking for the number of type definitions, 0x{typeDefinitionsCount:X}");
        var ptrsToNumberOfTypes = FindAllMappedWords((ulong)typeDefinitionsCount).ToList();

        LibLogger.VerboseNewline($"\t\t\tFound {ptrsToNumberOfTypes.Count} instances of the number of type definitions: [{string.Join(", ", ptrsToNumberOfTypes.Select(p => p.ToString("X")))}]");
        var possibleMetadataUsages = ptrsToNumberOfTypes.Select(a => a - sizeOfMr + ptrSize * 4).ToList();

        LibLogger.VerboseNewline($"\t\t\tFound {possibleMetadataUsages.Count} potential metadata registrations: [{string.Join(", ", possibleMetadataUsages.Select(p => p.ToString("X")))}]");

        var mrFieldCount = sizeOfMr / ptrSize;
        foreach (var va in possibleMetadataUsages)
        {
            try
            {
                var mrWords = binary.ReadNUintArrayAtVirtualAddress(va, (int)mrFieldCount);

                // Even field indices are counts, odd field indices are pointers
                var ok = true;
                for (var i = 0; i < mrWords.Length && ok; i++)
                {
                    if (i % 2 == 0)
                    {
                        //Count
                        ok = mrWords[i] < 0xC_0000;

                        if (!ok)
                            LibLogger.VerboseNewline($"\t\t\tRejected Metadata registration at 0x{va:X}, because it has a count field 0x{mrWords[i]:X} at offset {i} which is above sanity limit of 0xC0000. If metadata registration detection fails, may need to bump up the limit.");
                    }
                    else
                    {
                        //Pointer
                        if (mrWords[i] == 0)
                        {
                            ok = i >= 14; //Maybe need an investigation here, but metadataUsages can be (always is?) a null ptr on v27
                            if (!ok)
                                LibLogger.VerboseNewline($"\t\t\tRejecting metadata registration 0x{va:X} because the pointer at index {i} is 0.");
                        }
                        else
                        {
                            ok = binary.TryMapVirtualAddressToRaw((ulong)mrWords[i], out _); //Can be mapped successfully to the binary.
                            if (!ok)
                                LibLogger.VerboseNewline($"\t\t\tRejecting metadata registration 0x{va:X} because the pointer at index {i}, which is 0x{mrWords[i]:X}, can't be mapped to the binary.");
                        }
                    }

                    if (!ok)
                        break;
                }

                if (ok)
                {
                    var metaReg = binary.ReadReadableAtVirtualAddress<Il2CppMetadataRegistration>(va);
                    if (LibCpp2IlMain.MetadataVersion >= 27f && (metaReg.metadataUsagesCount != 0 || metaReg.metadataUsages != 0))
                    {
                        //Too many metadata usages - should be 0 on v27
                        LibLogger.VerboseNewline($"\t\t\tWarning: metadata registration 0x{va:X} has {metaReg.metadataUsagesCount} metadata usages at a pointer of 0x{metaReg.metadataUsages:X}. We're on v27, these should be 0.");
                        // continue;
                    }

                    if (metaReg.typeDefinitionsSizesCount != LibCpp2IlMain.TheMetadata!.typeDefs.Length)
                    {
                        LibLogger.VerboseNewline($"\t\t\tRejecting metadata registration 0x{va:X} because it has {metaReg.typeDefinitionsSizesCount} type def sizes, while metadata file defines {LibCpp2IlMain.TheMetadata!.typeDefs.Length} type defs");
                        continue;
                    }

                    if (metaReg.numTypes < LibCpp2IlMain.TheMetadata!.typeDefs.Length)
                    {
                        LibLogger.VerboseNewline($"\t\t\tRejecting metadata registration 0x{va:X} because it has {metaReg.numTypes} types, which is less than metadata-file-defined type def count of {LibCpp2IlMain.TheMetadata!.typeDefs.Length}");
                        continue;
                    }

                    if (metaReg.fieldOffsetsCount != LibCpp2IlMain.TheMetadata!.typeDefs.Length)
                    {
                        //If we see any cases of failing to find meta reg and this line is in verbose log, maybe the assumption (num field offsets == num type defs) is wrong.
                        LibLogger.VerboseNewline($"\t\t\tRejecting metadata registration 0x{va:X} because it has {metaReg.fieldOffsetsCount} field offsets, while metadata file defines {LibCpp2IlMain.TheMetadata!.typeDefs.Length} type defs");
                        continue;
                    }

                    LibLogger.VerboseNewline($"\t\t\tAccepting metadata reg as VA 0x{va:X}");
                    return va;
                }
            }
            catch (Exception e)
            {
                LibLogger.VerboseNewline($"\t\t\tRejecting metadata registration at 0x{va:X} because it threw an exception of type {e.GetType().FullName}");
            }
        }

        return 0;
    }
}
