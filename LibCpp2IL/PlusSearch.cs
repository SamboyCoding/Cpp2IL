using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text;
using LibCpp2IL.PE;
using SharpDisasm.Udis86;

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
                _pe.Position = (long) section.RawStartAddress;
                while ((ulong) _pe.Position < section.RawEndAddress)
                {
                    var addr = _pe.Position;
                    if (_pe.ReadUInt32() == methodCount)
                    {
                        try
                        {
                            var pointer = _pe.MapVirtualAddressToRaw(_pe.ReadUInt32());
                            if (CheckPointerInDataSection((ulong) pointer))
                            {
                                var pointers = _pe.ReadClassArray<uint>(pointer, methodCount);
                                if (CheckAllInExecSection(pointers))
                                {
                                    return (ulong) addr - section.RawStartAddress + section.VirtualStartAddress; //VirtualAddress
                                }
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    _pe.Position = addr + 4;
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
        private IEnumerable<ulong> FindAllMappedWords(IEnumerable<ulong> va) => va.SelectMany(a => FindAllMappedWords(a));

        internal ulong FindCodeRegistrationUsingMscorlib()
        {
            //Works only on >=24.2
            var searchBytes = Encoding.ASCII.GetBytes("mscorlib.dll\0");
            var mscorlibs = _pe.raw.Search(searchBytes).Select(idx => _pe.MapRawAddressToVirtual(idx));
            var usagesOfMscorlib = FindAllMappedWords(mscorlibs); //CodeGenModule address will be in here
            var codeGenModulesStructAddr = FindAllMappedWords(usagesOfMscorlib); //CodeGenModules list address will be in here
            var endOfCodeGenRegAddr = FindAllMappedWords(codeGenModulesStructAddr); //The end of the CodeRegistration object will be in here.

            if (!_pe.is32Bit)
            {
                var codeGenAddr = endOfCodeGenRegAddr.First();
                var textSection = _pe.sections.First(s => s.Name == ".text");
                var toDisasm = _pe.raw.SubArray((int) textSection.PointerToRawData, (int) textSection.SizeOfRawData);
                var allInstructionsInTextSection = LibCpp2ILUtils.DisassembleBytes(_pe.is32Bit, toDisasm);

                var allSensibleInstructions = allInstructionsInTextSection.AsParallel().Where(i =>
                    i.Mnemonic == ud_mnemonic_code.UD_Ilea
                    && !i.Error
                    && i.Operands.Length == 2
                    && i.Operands[0].Base == ud_type.UD_R_RCX).ToList();

                var sanity = 0;
                while (sanity++ < 500)
                {
                    var instruction = allSensibleInstructions.AsParallel().FirstOrDefault(i =>
                        textSection.VirtualAddress + _pe.imageBase + LibCpp2ILUtils.GetOffsetFromMemoryAccess(i, i.Operands[1]) == codeGenAddr
                    );

                    if (instruction != null) return codeGenAddr;

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

                    endOfCodeGenRegAddr = endOfCodeGenRegAddr.Select(va => va - 4);
                    codeRegVas = FindAllMappedWords(endOfCodeGenRegAddr);
                }

                if (endOfCodeGenRegAddr.Count() != 1)
                    return 0ul;

                return endOfCodeGenRegAddr.First();
            }
        }

        private ulong FindCodeRegistration64BitPost2019()
        {
            //NOTE: With 64-bit ELF binaries we should iterate on exec, on everything else data.
            var matchingAddresses = dataSections.Select(section =>
                {
                    _pe.Position = (long) section.RawStartAddress;
                    var secContent = _pe.ReadBytes((int) (section.RawEndAddress - section.RawStartAddress));

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
                    _pe.Position = (long) sec.RawStartAddress;
                    var positions = new List<ulong>();
                    while (_pe.Position < (long) sec.RawEndAddress)
                    {
                        if (_pe.ReadUInt64() == va)
                            positions.Add((ulong) _pe.Position - sec.RawStartAddress + sec.VirtualStartAddress);
                        _pe.Position += 8;
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
                _pe.Position = (long) section.RawStartAddress;
                while ((ulong) _pe.Position < section.RawEndAddress)
                {
                    var addr = _pe.Position;
                    //Check for the method count as an int64
                    if (_pe.ReadInt64() == methodCount)
                    {
                        try
                        {
                            //Should be followed by a pointer to the first function
                            var pointer = _pe.MapVirtualAddressToRaw(_pe.ReadUInt64());
                            //Which has to be in the data section
                            if (CheckPointerInDataSection((ulong) pointer))
                            {
                                var pointers = _pe.ReadClassArray<ulong>(pointer, methodCount);
                                if (CheckAllInExecSection(pointers))
                                {
                                    return (ulong) addr - section.RawStartAddress + section.VirtualStartAddress; //VirtualAddress
                                }
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    _pe.Position = addr + 8;
                }
            }

            return 0ul;
        }

        public ulong FindMetadataRegistration()
        {
            foreach (var section in search)
            {
                _pe.Position = (long) section.RawStartAddress;
                while ((ulong) _pe.Position < section.RawEndAddress)
                {
                    var addr = _pe.Position;
                    if (_pe.ReadInt32() == typeDefinitionsCount)
                    {
                        try
                        {
                            _pe.Position += 8;
                            long pointer = _pe.MapVirtualAddressToRaw(_pe.ReadUInt32());
                            if (CheckPointerInDataSection((ulong) pointer))
                            {
                                var pointers = _pe.ReadClassArray<uint>(pointer, maxMetadataUsages);
                                if (CheckAllInExecSection(pointers))
                                {
                                    return (ulong) addr - 48ul - section.RawStartAddress + section.VirtualStartAddress; //VirtualAddress
                                }
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    _pe.Position = addr + 4;
                }
            }

            return 0ul;
        }

        public ulong FindMetadataRegistration64Bit()
        {
            foreach (var section in search)
            {
                _pe.Position = (long) section.RawStartAddress;
                while ((ulong) _pe.Position < section.RawEndAddress)
                {
                    var addr = _pe.Position;
                    //Find an int64 equal to the type definition count
                    if (_pe.ReadInt64() == typeDefinitionsCount)
                    {
                        try
                        {
                            _pe.Position += 16;
                            var pointer = _pe.MapVirtualAddressToRaw(_pe.ReadUInt64());
                            if (CheckPointerInDataSection((ulong) pointer))
                            {
                                var pointers = _pe.ReadClassArray<ulong>(pointer, maxMetadataUsages);
                                if (CheckAllInExecSection(pointers))
                                {
                                    return (ulong) addr - 96ul - section.RawStartAddress + section.VirtualStartAddress; //VirtualAddress
                                }
                            }
                        }
                        catch
                        {
                            // ignored
                        }
                    }

                    _pe.Position = addr + 8;
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
    }
}