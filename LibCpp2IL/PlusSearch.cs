using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Iced.Intel;
using LibCpp2IL.PE;

namespace LibCpp2IL
{
    public class PlusSearch
    {
        private static readonly byte[] FeatureBytes2019 = {0x6D, 0x73, 0x63, 0x6F, 0x72, 0x6C, 0x69, 0x62, 0x2E, 0x64, 0x6C, 0x6C, 0x00};

        private class Section
        {
            public ulong RawStartAddress;
            public ulong RawEndAddress;
            public ulong VirtualStartAddress;
        }

        private PE.PE _pe;
        private int methodCount;
        private int typeDefinitionsCount;
        private long maxMetadataUsages;
        private List<Section> search = new List<Section>();
        private List<Section> dataSections = new List<Section>();
        private List<Section> execSections = new List<Section>();

        public PlusSearch(PE.PE pe, int methodCount, int typeDefinitionsCount, long maxMetadataUsages)
        {
            _pe = pe;
            this.methodCount = methodCount;
            this.typeDefinitionsCount = typeDefinitionsCount;
            this.maxMetadataUsages = maxMetadataUsages;
        }


        public void SetSearch(ulong imageBase, params SectionHeader[] sections)
        {
            foreach (var section in sections)
            {
                if (section != null)
                {
                    search.Add(new Section
                    {
                        RawStartAddress = section.PointerToRawData,
                        RawEndAddress = section.PointerToRawData + section.SizeOfRawData,
                        VirtualStartAddress = section.VirtualAddress + imageBase
                    });
                }
            }
        }

        public void SetDataSections(ulong imageBase, params SectionHeader[] sections)
        {
            foreach (var section in sections)
            {
                if (section != null)
                {
                    dataSections.Add(new Section
                    {
                        RawStartAddress = section.PointerToRawData,
                        RawEndAddress = section.PointerToRawData + section.SizeOfRawData,
                        VirtualStartAddress = section.VirtualAddress + imageBase
                    });
                }
            }
        }

        public void SetExecSections(ulong imageBase, params SectionHeader[] sections)
        {
            execSections.Clear();
            foreach (var section in sections)
            {
                if (section != null)
                {
                    execSections.Add(new Section
                    {
                        RawStartAddress = section.VirtualAddress,
                        RawEndAddress = section.VirtualAddress + section.VirtualSize + imageBase,
                        VirtualStartAddress = section.VirtualAddress + imageBase
                    });
                }
            }
        }

        public ulong FindCodeRegistration()
        {
            foreach (var section in search)
            {
                var position = section.RawStartAddress;
                while (position < section.RawEndAddress)
                {
                    if (_pe.ReadClass<uint>((long) position) == methodCount)
                    {
                        try
                        {
                            var pointer = _pe.MapVirtualAddressToRaw(_pe.ReadUInt32());
                            if (CheckPointerInDataSection((ulong) pointer))
                            {
                                var pointers = _pe.ReadClassArray<uint>(pointer, methodCount);
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

        public ulong FindCodeRegistration64Bit()
        {
            if (LibCpp2IlMain.MetadataVersion >= 24.2f)
                return FindCodeRegistration64BitPost2019();
            return FindCodeRegistration64BitPre2019();
        }

        // Find strings
        private IEnumerable<uint> FindAllStrings(string str) => _pe.raw.Search(Encoding.ASCII.GetBytes(str));

        // Find 32-bit words
        private IEnumerable<uint> FindAllDWords(uint word) => _pe.raw.Search(BitConverter.GetBytes(word));

        // Find 64-bit words
        private IEnumerable<uint> FindAllQWords(ulong word) => _pe.raw.Search(BitConverter.GetBytes(word));

        // Find words for the current binary size
        private IEnumerable<uint> FindAllWords(ulong word)
            => _pe.is32Bit ? FindAllDWords((uint) word) : FindAllQWords(word);

        // Find all valid virtual address pointers to a virtual address
        private IEnumerable<ulong> FindAllMappedWords(ulong va)
        {
            var fileOffsets = FindAllWords(va);
            foreach (var offset in fileOffsets)
                if (_pe.TryMapRawAddressToVirtual(offset, out va))
                    yield return va;
        }

        // Find all valid virtual address pointers to a set of virtual addresses
        private IEnumerable<ulong> FindAllMappedWords(IEnumerable<ulong> va) => va.SelectMany(FindAllMappedWords);

        public ulong TryFindCodeRegUsingMetaReg(ulong metadataRegistration)
        {
            var allInstructions = _pe.DisassembleTextSection();

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

        internal ulong FindCodeRegistrationUsingMscorlib()
        {
            //Works only on >=24.2
            var searchBytes = Encoding.ASCII.GetBytes("mscorlib.dll\0");
            var mscorlibs = _pe.raw.Search(searchBytes).Select(idx => _pe.MapRawAddressToVirtual(idx));
            var pMscorlibCodegenModule = FindAllMappedWords(mscorlibs); //CodeGenModule address will be in here
            var pMscorlibCodegenEntryInCodegenModulesList = FindAllMappedWords(pMscorlibCodegenModule); //CodeGenModules list address will be in here

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
                var pSomewhereInCodegenModules = pMscorlibCodegenEntryInCodegenModulesList;
                var ptrSize = (_pe.is32Bit ? 4u : 8u);
                for (var backtrack = 0; backtrack < sanityCheckNumberOfModules && (pCodegenModules?.Count() ?? 0) != 1; backtrack++)
                {
                    pCodegenModules = FindAllMappedWords(pSomewhereInCodegenModules);

                    //Sanity check the count, which is one pointer back
                    if (pCodegenModules.Count() == 1)
                    {
                        var moduleCount = _pe.ReadClassAtVirtualAddress<int>(pCodegenModules.First() - ptrSize);

                        if (moduleCount < 0 || moduleCount > sanityCheckNumberOfModules)
                            pCodegenModules = Enumerable.Empty<ulong>();
                    }

                    pSomewhereInCodegenModules = pSomewhereInCodegenModules.Select(va => va - ptrSize);
                }

                if (!pCodegenModules.Any())
                    throw new Exception("Failed to find pCodegenModules");

                if (pCodegenModules.Count() > 1)
                    throw new Exception("Found more than 1 pointer as pCodegenModules");
            }

            if (!_pe.is32Bit)
            {
                var codeGenAddr = pCodegenModules.First();
                var textSection = _pe.sections.First(s => s.Name == ".text");
                var toDisasm = _pe.raw.SubArray((int) textSection.PointerToRawData, (int) textSection.SizeOfRawData);
                var allInstructions = LibCpp2ILUtils.DisassembleBytesNew(_pe.is32Bit, toDisasm, textSection.VirtualAddress + _pe.imageBase);

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
            else
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
        }

        private ulong FindCodeRegistration64BitPost2019()
        {
            //NOTE: With 64-bit ELF binaries we should iterate on exec, on everything else data.
            var matchingAddresses = dataSections.Select(section =>
                {
                    var position = section.RawStartAddress;
                    var secContent = _pe.ReadByteArray((long) position, (int) (section.RawEndAddress - section.RawStartAddress));

                    //Find every virtual address of an occurrence of the search bytes
                    var virtualAddressesOfSearchBytes = secContent
                        .Search(FeatureBytes2019)
                        .Select(p => (ulong) p + section.VirtualStartAddress)
                        .ToList();

                    var locatedPositions = virtualAddressesOfSearchBytes.Select(va =>
                        {
                            var dataSectionsThatReferenceAddr = FindVirtualAddressesThatPointAtVirtualAddress(dataSections, va);

                            if (dataSectionsThatReferenceAddr.Count == 0) return 0UL;

                            //Now find all virtual addresses that point at THOSE addresses (what?)
                            dataSectionsThatReferenceAddr = dataSectionsThatReferenceAddr
                                .SelectMany(v => v.positions)
                                .SelectMany(a => FindVirtualAddressesThatPointAtVirtualAddress(dataSections, a))
                                .ToList();

                            if (dataSectionsThatReferenceAddr.Count == 0) return 0UL;

                            //And ANOTHER pass to find the virtual addresses that point at THOSE addresses. 
                            dataSectionsThatReferenceAddr = dataSectionsThatReferenceAddr
                                .SelectMany(v => v.positions)
                                .SelectMany(a => FindVirtualAddressesThatPointAtVirtualAddress(dataSections, a))
                                .ToList();

                            if (dataSectionsThatReferenceAddr.Count == 0) return 0UL;

                            //We want the first of those.
                            return dataSectionsThatReferenceAddr.First().positions.First();
                        })
                        .Where(p => p != 0UL)
                        .ToList();

                    //No matches => 0
                    if (locatedPositions.Count == 0) return 0UL;

                    //Assuming we have any matches return the first.
                    return locatedPositions.First();
                })
                .Where(p => p != 0UL)
                .ToList();

            if (matchingAddresses.Count == 0) return 0UL;

            var ret = matchingAddresses.First();

            if (LibCpp2IlMain.MetadataVersion > 24.2f) return ret - 120;

            return ret - 104;
        }

        private List<(Section sec, List<ulong> positions)> FindVirtualAddressesThatPointAtVirtualAddress(List<Section> sections, ulong va)
        {
            return sections.Select(sec =>
                {
                    //Find all virtual addresses that reference this search result
                    var position = (long) sec.RawStartAddress;
                    var positions = new List<ulong>();
                    while (position < (long) sec.RawEndAddress)
                    {
                        if (_pe.ReadClass<ulong>(position) == va)
                            positions.Add((ulong) _pe.Position - sec.RawStartAddress + sec.VirtualStartAddress);

                        position += 8;
                    }

                    return positions.Count > 0 ? (sec, positions) : default;
                })
                .Where(o => o != default)
                .ToList();
        }

        private ulong FindCodeRegistration64BitPre2019()
        {
            foreach (var section in search)
            {
                var position = section.RawStartAddress;
                while (position < section.RawEndAddress)
                {
                    var addr = position;
                    //Check for the method count as an int64
                    if (_pe.ReadClass<long>((long) position) == methodCount)
                    {
                        position += 4;
                        try
                        {
                            //Should be followed by a pointer to the first function
                            var pointer = _pe.MapVirtualAddressToRaw(_pe.ReadClass<ulong>((long) position));
                            //Which has to be in the data section
                            if (CheckPointerInDataSection((ulong) pointer))
                            {
                                var pointers = _pe.ReadClassArray<ulong>(pointer, methodCount);
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

                    position = addr + 4;
                }
            }

            return 0ul;
        }

        public ulong FindMetadataRegistration()
        {
            foreach (var section in search)
            {
                var position = (long) section.RawStartAddress;
                while ((ulong) _pe.Position < section.RawEndAddress)
                {
                    var addr = position;
                    if (_pe.ReadClass<int>(position) == typeDefinitionsCount)
                    {
                        position += 4; //For the int 
                        try
                        {
                            position += 16; //Move to pMetadataUsages
                            var pointer = _pe.MapVirtualAddressToRaw(_pe.ReadClass<uint>(position));
                            if (CheckPointerInDataSection((ulong) pointer))
                            {
                                var pointers = _pe.ReadClassArray<uint>(pointer, maxMetadataUsages);
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
            foreach (var section in search)
            {
                var position = section.RawStartAddress;
                while (position < section.RawEndAddress)
                {
                    var addr = position;
                    //Find an int64 equal to the type definition count
                    if (_pe.ReadClass<long>((long) position) == typeDefinitionsCount)
                    {
                        try
                        {
                            position += 16;
                            var pointer = _pe.MapVirtualAddressToRaw(_pe.ReadClass<ulong>((long) position));
                            if (CheckPointerInDataSection((ulong) pointer))
                            {
                                var pointers = _pe.ReadClassArray<ulong>(pointer, maxMetadataUsages);
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

        private bool CheckPointerInDataSection(ulong pointer)
        {
            return dataSections.Any(x => pointer >= x.RawStartAddress && pointer <= x.RawEndAddress);
        }

        private bool CheckAllInExecSection(IEnumerable<ulong> pointers)
        {
            return pointers.All(x => execSections.Any(y => x >= y.RawStartAddress && x <= y.RawEndAddress));
        }

        private bool CheckAllInExecSection(IEnumerable<uint> pointers)
        {
            return pointers.All(x => execSections.Any(y => x >= y.RawStartAddress && x <= y.RawEndAddress));
        }

        public ulong FindMetadataRegistrationV27()
        {
            var ptrSize = _pe.is32Bit ? 4u : 8u;
            var sizeOfMr = (uint) LibCpp2ILUtils.VersionAwareSizeOf(typeof(Il2CppMetadataRegistration));
            var ptrsToNumberOfTypes = FindAllMappedWords((ulong) typeDefinitionsCount);

            var possibleMetadataUsages = ptrsToNumberOfTypes.Select(a => a - sizeOfMr + ptrSize * 4);

            var mrFieldCount = sizeOfMr / (ulong) (ptrSize);
            foreach (var va in possibleMetadataUsages)
            {
                var mrWords = _pe.ReadClassArrayAtVirtualAddress<long>(va, (int) mrFieldCount);

                // Even field indices are counts, odd field indices are pointers
                var ok = true;
                for (var i = 0; i < mrWords.Length && ok; i++)
                {
                    ok = i % 2 == 0 ? mrWords[i] < 0x30000 : mrWords[i] == 0 || _pe.TryMapVirtualAddressToRaw((ulong) mrWords[i], out _); //Maybe need an investigation here, but metadataUsages can be a null ptr.
                }

                if (ok)
                    return va;
            }

            return 0;
        }
    }
}