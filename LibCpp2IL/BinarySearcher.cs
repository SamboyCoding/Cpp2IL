using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Iced.Intel;
using LibCpp2IL.Elf;
using LibCpp2IL.PE;

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
        private readonly long maxMetadataUsages;

        private readonly List<Section> _searchSections = new();
        private readonly List<Section> _dataSections = new();
        private readonly List<Section> _execSections = new();

        public BinarySearcher(PE.PE pe, int methodCount, int typeDefinitionsCount, long maxMetadataUsages)
        {
            _binary = pe;
            binaryBytes = pe.raw;
            this.methodCount = methodCount;
            this.typeDefinitionsCount = typeDefinitionsCount;
            this.maxMetadataUsages = maxMetadataUsages;
        }

        public BinarySearcher(Il2CppBinary binary, int methodCount, int typeDefinitionsCount, long maxMetadataUsages)
        {
            _binary = binary;
            binaryBytes = binary.GetRawBinaryContent();
            this.methodCount = methodCount;
            this.typeDefinitionsCount = typeDefinitionsCount;
            this.maxMetadataUsages = maxMetadataUsages;
        }


        public void SetSearchSectionsFromPe(ulong imageBase, params SectionHeader[] sections)
        {
            foreach (var section in sections)
            {
                _searchSections.Add(new Section
                {
                    RawStartAddress = section.PointerToRawData,
                    RawEndAddress = section.PointerToRawData + section.SizeOfRawData,
                    VirtualStartAddress = section.VirtualAddress + imageBase
                });
            }
        }

        public void SetDataSectionsFromPe(ulong imageBase, params SectionHeader[] sections)
        {
            foreach (var section in sections)
            {
                _dataSections.Add(new Section
                {
                    RawStartAddress = section.PointerToRawData,
                    RawEndAddress = section.PointerToRawData + section.SizeOfRawData,
                    VirtualStartAddress = section.VirtualAddress + imageBase
                });
            }
        }

        public void SetExecSectionsFromPe(ulong imageBase, params SectionHeader[] sections)
        {
            _execSections.Clear();
            foreach (var section in sections)
            {
                _execSections.Add(new Section
                {
                    RawStartAddress = section.VirtualAddress,
                    RawEndAddress = section.VirtualAddress + section.VirtualSize + imageBase,
                    VirtualStartAddress = section.VirtualAddress + imageBase
                });
            }
        }

        public ulong FindCodeRegistrationUsingMethodCount()
        {
            foreach (var section in _searchSections)
            {
                var position = section.RawStartAddress;
                while (position < section.RawEndAddress)
                {
                    if (_binary.ReadClassAtRawAddr<uint>((long) position) == methodCount)
                    {
                        try
                        {
                            var pointer = _binary.MapVirtualAddressToRaw(_binary.ReadUInt32());
                            if (CheckPointerInDataSection((ulong) pointer))
                            {
                                var pointers = _binary.ReadClassArrayAtRawAddr<uint>(pointer, methodCount);
                                if (CheckAllInExecSection(pointers))
                                {
                                    return position - section.RawStartAddress + section.VirtualStartAddress; //VirtualAddress
                                }
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    position += 4;
                }
            }

            return 0ul;
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

        //Try to find the code registration via the codereg function, by looking for a push on the meta reg. Will only work on x86_32.
        public ulong TryFindCodeRegUsingFunctionAndMetaRegX86_32(ulong metadataRegistration)
        {
            if (!(_binary is PE.PE pe))
                return 0;

            var allInstructions = pe.DisassembleTextSection();

            var pushMetaReg = allInstructions.FirstOrDefault(i => i.Mnemonic == Mnemonic.Push && i.Op0Kind.IsImmediate() && i.GetImmediate(0) == metadataRegistration);
            if (pushMetaReg.Mnemonic == Mnemonic.Push) //Check non-default.
            {
                var idx = allInstructions.IndexOf(pushMetaReg);
                //Code reg has to be pushed after meta reg, cause that's how functions are called on 32-bit stdcall.
                var hopefullyPushCodeReg = allInstructions[idx + 1];
                if (hopefullyPushCodeReg.Mnemonic == Mnemonic.Push && hopefullyPushCodeReg.Op0Kind.IsImmediate())
                    return hopefullyPushCodeReg.GetImmediate(0);
            }

            return 0;
        }

        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        internal ulong FindCodeRegistrationUsingMscorlib()
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
                case InstructionSet.X86_64:
                {
                    if (!(_binary is PE.PE pe)) return 0;

                    var codeGenAddr = pCodegenModules.First();
                    var allInstructions = pe.DisassembleTextSection();

                    var allSensibleInstructions = allInstructions.Where(i =>
                            i.Mnemonic == Mnemonic.Lea
                            && i.OpCount == 2
                            && i.Op0Kind == OpKind.Register
                            && i.Op1Kind == OpKind.Memory
                        /*&& i.Op0Register == Register.RCX*/).ToList();

                    var sanity = 0;
                    while (sanity++ < 500)
                    {
                        var instruction = allSensibleInstructions.FirstOrDefault(i =>
                            i.GetRipBasedInstructionMemoryAddress() == codeGenAddr
                        );

                        if (instruction != default) return codeGenAddr;

                        codeGenAddr -= 8; //Always 64-bit here so IntPtr is 8
                    }

                    return 0;
                }
                case InstructionSet.X86_32:
                {
                    IEnumerable<ulong>? codeRegVas = null;
                    var sanity = 0;
                    while ((codeRegVas?.Count() ?? 0) != 1)
                    {
                        if (sanity++ > 500) break;

                        pCodegenModules = pCodegenModules.Select(va => va - 4);
                        codeRegVas = FindAllMappedWords(pCodegenModules).AsParallel().ToList();
                    }

                    if (pCodegenModules.Count() != 1)
                        return 0ul;

                    return pCodegenModules.First();
                }
                default:
                    //We have pCodegenModules which *should* be x-reffed in the last pointer of Il2CppCodeRegistration.
                    //So, subtract the size of one pointer from that...
                    var bytesToGoBack = (ulong) LibCpp2ILUtils.VersionAwareSizeOf(typeof(Il2CppCodeRegistration)) - ptrSize;

                    //And subtract that from our pointer.
                    return pCodegenModules.First() - bytesToGoBack;
            }
        }

        public ulong FindCodeRegistration64BitPre2019()
        {
            foreach (var section in _searchSections)
            {
                var position = section.RawStartAddress;
                while (position < section.RawEndAddress)
                {
                    var addr = position;
                    //Check for the method count as an int64
                    if (_binary.ReadClassAtRawAddr<long>((long) position) == methodCount)
                    {
                        position += 8; //For the long
                        try
                        {
                            //Should be followed by a pointer to the first function
                            var pointer = _binary.MapVirtualAddressToRaw(_binary.ReadClassAtRawAddr<ulong>((long) position));
                            //Which has to be in the data section
                            if (CheckPointerInDataSection((ulong) pointer))
                            {
                                var pointers = _binary.ReadClassArrayAtRawAddr<ulong>(pointer, methodCount);
                                if (CheckAllInExecSection(pointers))
                                {
                                    return addr - section.RawStartAddress + section.VirtualStartAddress; //VirtualAddress
                                }
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    position = addr + 4;
                }
            }

            return 0ul;
        }

        public ulong FindMetadataRegistrationAuto()
        {
            if (LibCpp2IlMain.MetadataVersion >= 27f)
                return FindMetadataRegistrationV27();

            if (_binary.is32Bit)
                return FindMetadataRegistration();

            return FindMetadataRegistration64Bit();
        }

        public ulong FindMetadataRegistration()
        {
            foreach (var section in _searchSections)
            {
                var position = (long) section.RawStartAddress;
                while ((ulong) _binary.Position < section.RawEndAddress)
                {
                    var addr = position;
                    if (_binary.ReadClassAtRawAddr<int>(position) == typeDefinitionsCount)
                    {
                        position += 4; //For the int 
                        try
                        {
                            position += 16; //Move to pMetadataUsages
                            var pointer = _binary.MapVirtualAddressToRaw(_binary.ReadClassAtRawAddr<uint>(position));
                            if (CheckPointerInDataSection((ulong) pointer))
                            {
                                var pointers = _binary.ReadClassArrayAtRawAddr<uint>(pointer, maxMetadataUsages);
                                if (CheckAllInExecSection(pointers))
                                {
                                    return (ulong) addr - 40ul - section.RawStartAddress + section.VirtualStartAddress; //VirtualAddress
                                }
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    position = addr + 4;
                }
            }

            return 0ul;
        }

        public ulong FindMetadataRegistration64Bit()
        {
            foreach (var section in _searchSections)
            {
                var position = section.RawStartAddress;
                while (position < section.RawEndAddress)
                {
                    var addr = position;
                    //Find an int64 equal to the type definition count
                    if (_binary.ReadClassAtRawAddr<long>((long) position) == typeDefinitionsCount)
                    {
                        position += 8; //For the long
                        try
                        {
                            position += 16;
                            var pointer = _binary.MapVirtualAddressToRaw(_binary.ReadClassAtRawAddr<ulong>((long) position));
                            if (CheckPointerInDataSection((ulong) pointer))
                            {
                                var pointers = _binary.ReadClassArrayAtRawAddr<ulong>(pointer, maxMetadataUsages);
                                if (CheckAllInExecSection(pointers))
                                {
                                    return addr - 96ul - section.RawStartAddress + section.VirtualStartAddress; //VirtualAddress
                                }
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    position = addr + 8;
                }
            }

            return 0ul;
        }

        public ulong FindMetadataRegistrationNewApproachPre27()
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

        private bool CheckPointerInDataSection(ulong pointer)
        {
            return _dataSections.Any(x => pointer >= x.RawStartAddress && pointer <= x.RawEndAddress);
        }

        private bool CheckAllInExecSection(IEnumerable<ulong> pointers)
        {
            return pointers.All(x => _execSections.Any(y => x >= y.RawStartAddress && x <= y.RawEndAddress));
        }

        private bool CheckAllInExecSection(IEnumerable<uint> pointers)
        {
            return pointers.All(x => _execSections.Any(y => x >= y.RawStartAddress && x <= y.RawEndAddress));
        }

        public ulong FindMetadataRegistrationV27()
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