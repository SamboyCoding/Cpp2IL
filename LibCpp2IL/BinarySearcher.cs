using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Logging;

namespace LibCpp2IL
{
    public class BinarySearcher
    {
        private static readonly byte[] FeatureBytes2019 = {0x6D, 0x73, 0x63, 0x6F, 0x72, 0x6C, 0x69, 0x62, 0x2E, 0x64, 0x6C, 0x6C, 0x00};

        private class Section
        {
            public ulong RawStartAddress;
            public ulong RawEndAddress;
            public ulong VirtualStartAddress;
        }

        private readonly Il2CppBinary _binary;
        private readonly byte[] binaryBytes;
        private readonly int methodCount;
        private readonly int typeDefinitionsCount;

        public BinarySearcher(Il2CppBinary binary, int methodCount, int typeDefinitionsCount)
        {
            _binary = binary;
            binaryBytes = binary.GetRawBinaryContent();
            this.methodCount = methodCount;
            this.typeDefinitionsCount = typeDefinitionsCount;
        }
        
        private int FindBytes(byte[] blob, byte[] signature, int requiredAlignment = 1, int startOffset = 0) {
            var firstMatchByte = Array.IndexOf(blob, signature[0], startOffset);
            var test = new byte[signature.Length];

            while (firstMatchByte >= 0 && firstMatchByte <= blob.Length - signature.Length) {
                Buffer.BlockCopy(blob, firstMatchByte, test, 0, signature.Length);
                if (firstMatchByte % requiredAlignment == 0 && test.SequenceEqual(signature))
                    return firstMatchByte;

                firstMatchByte = Array.IndexOf(blob, signature[0], firstMatchByte + 1);
            }
            return -1;
        }

        // Find all occurrences of a sequence of bytes, using word alignment by default
        private IEnumerable<uint> FindAllBytes(byte[] signature, int alignment = 0) {
            LibLogger.VerboseNewline($"\t\t\tLooking for bytes: {string.Join(" ", signature.Select(b => b.ToString("x2")))}");
            var offset = 0;
            var ptrSize = _binary.is32Bit ? 4 : 8;
            while (offset != -1) {
                offset = FindBytes(binaryBytes, signature, alignment != 0 ? alignment : ptrSize, offset);
                if (offset != -1) {
                    yield return (uint) offset;
                    offset += ptrSize;
                }
            }
        }

        // Find strings
        private IEnumerable<uint> FindAllStrings(string str) => FindAllBytes(Encoding.ASCII.GetBytes(str), 1);

        // Find 32-bit words
        private IEnumerable<uint> FindAllDWords(uint word) => FindAllBytes(BitConverter.GetBytes(word), 1);

        // Find 64-bit words
        private IEnumerable<uint> FindAllQWords(ulong word) => FindAllBytes(BitConverter.GetBytes(word), 1);

        // Find words for the current binary size
        private IEnumerable<uint> FindAllWords(ulong word)
            => _binary.is32Bit ? FindAllDWords((uint) word) : FindAllQWords(word);

        private IEnumerable<ulong> MapOffsetsToVirt(IEnumerable<uint> offsets)
        {
            foreach (var offset in offsets)
                if (_binary.TryMapRawAddressToVirtual(offset, out var word))
                    yield return word;
        }

        // Find all valid virtual address pointers to a virtual address
        private IEnumerable<ulong> FindAllMappedWords(ulong word)
        {
            var fileOffsets = FindAllWords(word);
            return MapOffsetsToVirt(fileOffsets);
        }

        // Find all valid virtual address pointers to a set of virtual addresses
        private IEnumerable<ulong> FindAllMappedWords(IEnumerable<ulong> va) => va.SelectMany(FindAllMappedWords);
        
        private IEnumerable<ulong> FindAllMappedWords(IEnumerable<uint> va) => va.SelectMany(a => FindAllMappedWords(a));

        public ulong FindCodeRegistrationPre2019()
        {
            //First item in the CodeRegistration is the number of methods.
            var vas = MapOffsetsToVirt(FindAllBytes(BitConverter.GetBytes(methodCount), 1)).ToList();
            
            LibLogger.VerboseNewline($"\t\t\tFound {vas.Count} instances of the method count {methodCount}, as bytes {string.Join(", ", BitConverter.GetBytes((ulong) methodCount).Select(x => $"0x{x:X}"))}");

            if (vas.Count == 0)
                return 0;
            
            foreach (var va in vas)
            {
                var cr = _binary.ReadClassAtVirtualAddress<Il2CppCodeRegistration>(va);

                if ((long) cr.customAttributeCount == LibCpp2IlMain.TheMetadata!.attributeTypeRanges.Length)
                    return va;
            }

            return 0;
        }

        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        internal ulong FindCodeRegistrationPost2019()
        {
            //Works only on >=24.2
            var mscorlibs = FindAllStrings("mscorlib.dll\0").Select(idx => _binary.MapRawAddressToVirtual(idx)).ToList();
            
            LibLogger.VerboseNewline($"\t\t\tFound {mscorlibs.Count} occurrences of mscorlib.dll");
            
            var pMscorlibCodegenModule = FindAllMappedWords(mscorlibs).ToList(); //CodeGenModule address will be in here
            
            LibLogger.VerboseNewline($"\t\t\tFound {pMscorlibCodegenModule.Count} potential codegen modules for mscorlib");
            
            var pMscorlibCodegenEntryInCodegenModulesList = FindAllMappedWords(pMscorlibCodegenModule).ToList(); //CodeGenModules list address will be in here
            
            LibLogger.VerboseNewline($"\t\t\tFound {pMscorlibCodegenEntryInCodegenModulesList.Count} address for potential codegen modules in potential codegen module lists");
            
            var ptrSize = (_binary.is32Bit ? 4u : 8u);

            List<ulong>? pCodegenModules = null;
            if (!(LibCpp2IlMain.MetadataVersion >= 27f))
            {
                //Pre-v27, mscorlib is the first codegen module, so *MscorlibCodegenEntryInCodegenModulesList == g_CodegenModules, so we can just find a pointer to this.
                pCodegenModules = FindAllMappedWords(pMscorlibCodegenEntryInCodegenModulesList).ToList();
            }
            else
            {
                //but in v27 it's close to the LAST codegen module (winrt.dll is an exception), so we need to work back until we find an xref.
                var sanityCheckNumberOfModules = 200;
                var pSomewhereInCodegenModules = pMscorlibCodegenEntryInCodegenModulesList.AsEnumerable();
                for (var backtrack = 0; backtrack < sanityCheckNumberOfModules && (pCodegenModules?.Count() ?? 0) != 1; backtrack++)
                {
                    pCodegenModules = FindAllMappedWords(pSomewhereInCodegenModules).ToList();

                    //Sanity check the count, which is one pointer back
                    if (pCodegenModules.Count == 1)
                    {
                        var moduleCount = _binary.ReadClassAtVirtualAddress<int>(pCodegenModules.First() - ptrSize);

                        if (moduleCount < 0 || moduleCount > sanityCheckNumberOfModules)
                            pCodegenModules = new();
                    }

                    pSomewhereInCodegenModules = pSomewhereInCodegenModules.Select(va => va - ptrSize);
                }

                if (pCodegenModules?.Any() != true)
                    throw new Exception("Failed to find pCodegenModules");

                if (pCodegenModules.Count() > 1)
                    throw new Exception("Found more than 1 pointer as pCodegenModules");
            }

            switch (_binary.InstructionSet)
            {
                // case InstructionSet.X86_64:
                // {
                //     if (!(_binary is PE.PE pe)) return 0;
                //
                //     var codeGenAddr = pCodegenModules.First();
                //     var allInstructions = pe.DisassembleTextSection();
                //
                //     var allSensibleInstructions = allInstructions.Where(i =>
                //             i.Mnemonic == Mnemonic.Lea
                //             && i.OpCount == 2
                //             && i.Op0Kind == OpKind.Register
                //             && i.Op1Kind == OpKind.Memory
                //         /*&& i.Op0Register == Register.RCX*/).ToList();
                //
                //     var sanity = 0;
                //     while (sanity++ < 500)
                //     {
                //         var instruction = allSensibleInstructions.FirstOrDefault(i =>
                //             i.GetRipBasedInstructionMemoryAddress() == codeGenAddr
                //         );
                //
                //         if (instruction != default) return codeGenAddr;
                //
                //         codeGenAddr -= 8; //Always 64-bit here so IntPtr is 8
                //     }
                //
                //     return 0;
                // }
                default:
                    //We have pCodegenModules which *should* be x-reffed in the last pointer of Il2CppCodeRegistration.
                    //So, subtract the size of one pointer from that...
                    var bytesToGoBack = (ulong) LibCpp2ILUtils.VersionAwareSizeOf(typeof(Il2CppCodeRegistration)) - ptrSize;
                    
                    LibLogger.VerboseNewline($"\t\t\tpCodegenModules is the second-to-last field of the codereg struct. Therefore on this version and architecture, we need to subtract {bytesToGoBack} bytes from its address to get pCodeReg");

                    var fields = typeof(Il2CppCodeRegistration).GetFields();
                    var fieldsByName = fields.ToDictionary(f => f.Name);
                    
                    //Sanity checks
                    foreach (var pCodegenModule in pCodegenModules)
                    {
                        var address = pCodegenModule - bytesToGoBack;
                        LibLogger.Verbose($"\t\t\tConsidering potential code registration at 0x{address:X}...");

                        var codeReg = LibCpp2IlMain.Binary!.ReadClassAtVirtualAddress<Il2CppCodeRegistration>(address);

                        var success = true;
                        foreach (var keyValuePair in fieldsByName)
                        {
                            var fieldValue = (ulong) keyValuePair.Value.GetValue(codeReg);
                            
                            if(fieldValue == 0)
                                continue; //Allow zeroes
                            
                            if (keyValuePair.Key.EndsWith("count", StringComparison.OrdinalIgnoreCase))
                            {
                                if (fieldValue > 0x60_000)
                                {
                                    LibLogger.VerboseNewline($"Rejected due to unreasonable count field 0x{fieldValue:X} for field {keyValuePair.Key}");
                                    success = false;
                                    break;
                                }
                            }
                            else
                            {
                                //Pointer
                                if (!LibCpp2IlMain.Binary.TryMapVirtualAddressToRaw(fieldValue, out _))
                                {
                                    LibLogger.VerboseNewline($"Rejected due to invalid pointer 0x{fieldValue:X} for field {keyValuePair.Key}");
                                    success = false;
                                    break;
                                }
                            }
                        }

                        if (success)
                        {
                            LibLogger.VerboseNewline("Looks good!");
                            return address;
                        }
                    }

                    //And subtract that from our pointer.
                    return pCodegenModules.First() - bytesToGoBack;
            }
        }

        public ulong FindMetadataRegistrationPre24_5()
        {
            //We're looking for TypeDefinitionsSizesCount, which is the 4th-to-last field
            var sizeOfMr = (ulong) LibCpp2ILUtils.VersionAwareSizeOf(typeof(Il2CppMetadataRegistration));
            var ptrSize = _binary.is32Bit ? 4ul : 8ul;

            var bytesToSubtract = sizeOfMr - ptrSize * 4;
            
            var potentialMetaRegPointers = MapOffsetsToVirt(FindAllBytes(BitConverter.GetBytes(LibCpp2IlMain.TheMetadata!.typeDefs.Length), 1)).ToList();
            
            LibLogger.VerboseNewline($"\t\t\tFound {potentialMetaRegPointers.Count} instances of the number of type defs, {LibCpp2IlMain.TheMetadata.typeDefs.Length}");

            potentialMetaRegPointers = potentialMetaRegPointers.Select(p => p - bytesToSubtract).ToList();

            foreach (var potentialMetaRegPointer in potentialMetaRegPointers)
            {
                var mr = _binary.ReadClassAtVirtualAddress<Il2CppMetadataRegistration>(potentialMetaRegPointer);
                
                if (mr.metadataUsagesCount == (ulong)LibCpp2IlMain.TheMetadata!.metadataUsageLists.Length) 
                    return potentialMetaRegPointer;
                else
                    LibLogger.VerboseNewline($"\t\t\tSkipping 0x{potentialMetaRegPointer:X} as the metadata reg, metadata usage count was 0x{mr.metadataUsagesCount:X}, expecting 0x{LibCpp2IlMain.TheMetadata.metadataUsageLists.Length:X}");
            }

            return 0;
        }

        public ulong FindMetadataRegistrationPost24_5()
        {
            var ptrSize = _binary.is32Bit ? 4ul : 8ul;
            var sizeOfMr = (uint) LibCpp2ILUtils.VersionAwareSizeOf(typeof(Il2CppMetadataRegistration));
            var ptrsToNumberOfTypes = FindAllMappedWords((ulong) typeDefinitionsCount);

            var possibleMetadataUsages = ptrsToNumberOfTypes.Select(a => a - sizeOfMr + ptrSize * 4);

            var mrFieldCount = sizeOfMr / ptrSize;
            foreach (var va in possibleMetadataUsages)
            {
                var mrWords = _binary.ReadClassArrayAtVirtualAddress<long>(va, (int) mrFieldCount);

                // Even field indices are counts, odd field indices are pointers
                var ok = true;
                for (var i = 0; i < mrWords.Length && ok; i++)
                {
                    if (i % 2 == 0)
                    {
                        //Count
                        ok = mrWords[i] < 0xA_0000 && mrWords[i] >= 0;

                        if (!ok && mrWords[i] < 0xF_FFFF) 
                            LibLogger.InfoNewline($"\tWARNING: Metadata Usage count field skipped as unreasonable because it is 0x{mrWords[i]:X} which is above sanity limit of 0xA0000. If metadata registration detection fails, need to bump up the limit.");
                    }
                    else
                    {
                        //Pointer
                        if (mrWords[i] == 0)
                            ok = i >= 14; //Maybe need an investigation here, but metadataUsages can be (always is?) a null ptr on v27
                        else 
                            ok = _binary.TryMapVirtualAddressToRaw((ulong) mrWords[i], out _); //Can be mapped successfully to the binary. 
                    }
                }

                if (ok)
                {
                    return va;
                }
            }

            return 0;
        }
    }
}