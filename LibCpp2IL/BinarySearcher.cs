using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Iced.Intel;
using LibCpp2IL.BinaryStructures;

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
        private IEnumerable<uint> FindAllDWords(uint word) => FindAllBytes(BitConverter.GetBytes(word), 4);

        // Find 64-bit words
        private IEnumerable<uint> FindAllQWords(ulong word) => FindAllBytes(BitConverter.GetBytes(word), 8);

        // Find words for the current binary size
        private IEnumerable<uint> FindAllWords(ulong word)
            => _binary.is32Bit ? FindAllDWords((uint) word) : FindAllQWords(word);

        // Find all valid virtual address pointers to a virtual address
        private IEnumerable<ulong> FindAllMappedWords(ulong va)
        {
            var fileOffsets = FindAllWords(va);
            foreach (var offset in fileOffsets)
                if (_binary.TryMapRawAddressToVirtual(offset, out va))
                    yield return va;
        }

        // Find all valid virtual address pointers to a set of virtual addresses
        private IEnumerable<ulong> FindAllMappedWords(IEnumerable<ulong> va) => va.SelectMany(FindAllMappedWords);

        public ulong FindCodeRegistrationPre2019()
        {
            //First item in the CodeRegistration is the number of methods.
            var vas = FindAllMappedWords((ulong) methodCount).ToList();

            if (vas.Count == 0)
                return 0;
            
            foreach (var va in vas)
            {
                var cr = _binary.ReadClassAtVirtualAddress<Il2CppCodeRegistration>(va);

                if (cr.customAttributeCount == LibCpp2IlMain.TheMetadata!.attributeTypeRanges.Length)
                    return va;
            }

            return 0;
        }

        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        internal ulong FindCodeRegistrationPost2019()
        {
            //Works only on >=24.2
            var mscorlibs = FindAllStrings("mscorlib.dll\0").Select(idx => _binary.MapRawAddressToVirtual(idx));
            var pMscorlibCodegenModule = FindAllMappedWords(mscorlibs); //CodeGenModule address will be in here
            var pMscorlibCodegenEntryInCodegenModulesList = FindAllMappedWords(pMscorlibCodegenModule).ToList(); //CodeGenModules list address will be in here
            var ptrSize = (_binary.is32Bit ? 4u : 8u);

            IEnumerable<ulong>? pCodegenModules = null;
            if (!(LibCpp2IlMain.MetadataVersion >= 27f))
            {
                //Pre-v27, mscorlib is the first codegen module, so *MscorlibCodegenEntryInCodegenModulesList == g_CodegenModules, so we can just find a pointer to this.
                var intermediate = pMscorlibCodegenEntryInCodegenModulesList;
                pCodegenModules = FindAllMappedWords(intermediate);
            }
            else
            {
                //but in v27 it's close to the LAST codegen module (winrt.dll is an exception), so we need to work back until we find an xref.
                var sanityCheckNumberOfModules = 200;
                var pSomewhereInCodegenModules = pMscorlibCodegenEntryInCodegenModulesList.AsEnumerable();
                for (var backtrack = 0; backtrack < sanityCheckNumberOfModules && (pCodegenModules?.Count() ?? 0) != 1; backtrack++)
                {
                    pCodegenModules = FindAllMappedWords(pSomewhereInCodegenModules);

                    //Sanity check the count, which is one pointer back
                    if (pCodegenModules.Count() == 1)
                    {
                        var moduleCount = _binary.ReadClassAtVirtualAddress<int>(pCodegenModules.First() - ptrSize);

                        if (moduleCount < 0 || moduleCount > sanityCheckNumberOfModules)
                            pCodegenModules = Enumerable.Empty<ulong>();
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

                    //And subtract that from our pointer.
                    return pCodegenModules.First() - bytesToGoBack;
            }
        }

        public ulong FindMetadataRegistrationPre27()
        {
            //We're looking for TypeDefinitionsSizesCount, which is the 4th-to-last field
            var sizeOfMr = (ulong) LibCpp2ILUtils.VersionAwareSizeOf(typeof(Il2CppMetadataRegistration));
            var ptrSize = _binary.is32Bit ? 4ul : 8ul;

            var bytesToSubtract = sizeOfMr - ptrSize * 4;

            var potentialMetaRegPointers = FindAllMappedWords((ulong) LibCpp2IlMain.TheMetadata!.typeDefs.Length);

            potentialMetaRegPointers = potentialMetaRegPointers.Select(p => p - bytesToSubtract);

            return (from potentialMetaRegPointer in potentialMetaRegPointers
                    let mr = _binary.ReadClassAtVirtualAddress<Il2CppMetadataRegistration>(potentialMetaRegPointer)
                    where mr.metadataUsagesCount == (ulong) LibCpp2IlMain.TheMetadata!.metadataUsageLists.Length
                    select potentialMetaRegPointer)
                .FirstOrDefault();
        }

        public ulong FindMetadataRegistrationPost27()
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
                        ok = mrWords[i] < 0x30000;
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
                    return va;
            }

            return 0;
        }
    }
}