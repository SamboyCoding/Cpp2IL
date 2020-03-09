using System.Collections.Generic;
using System.Linq;
using Cpp2IL.PE;

namespace Cpp2IL
{
    public class PlusSearch
    {
        private static readonly byte[] featureBytes2019 = {0x6D, 0x73, 0x63, 0x6F, 0x72, 0x6C, 0x69, 0x62, 0x2E, 0x64, 0x6C, 0x6C, 0x00};

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
                            uint pointer = _pe.MapVirtualAddressToRaw(_pe.ReadUInt32());
                            if (CheckPointerInDataSection(pointer))
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
            if (Program.MetadataVersion >= 24.2f)
                return FindCodeRegistration64BitPost2019();
            return FindCodeRegistration64BitPre2019();
        }

        private ulong FindCodeRegistration64BitPost2019()
        {
            //NOTE: With 64-bit ELF binaries we should iterate on exec, on everything else data.
            var matchingAddresses = dataSections.Select(section =>
                {
                    _pe.Position = (long) section.RawStartAddress;
                    var secContent = _pe.ReadBytes((int) (section.RawEndAddress - section.RawStartAddress));

                    //Find every virtual address of an occurrence of the search bytes
                    var virtualAddressesOfSearchBytes = secContent.Search(featureBytes2019).Select(p => (ulong) p + section.VirtualStartAddress).ToList();

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

            var ret = matchingAddresses.Count == 0 ? 0UL : matchingAddresses.First();

            if (Program.MetadataVersion > 24.2f) return ret - 120;

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
                    if (_pe.ReadInt64() == methodCount)
                    {
                        try
                        {
                            ulong pointer = _pe.MapVirtualAddressToRaw(_pe.ReadUInt64());
                            if (CheckPointerInDataSection(pointer))
                            {
                                var pointers = _pe.ReadClassArray<ulong>((long) pointer, methodCount);
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
                            uint pointer = _pe.MapVirtualAddressToRaw(_pe.ReadUInt32());
                            if (CheckPointerInDataSection(pointer))
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
                    if (_pe.ReadInt64() == typeDefinitionsCount)
                    {
                        try
                        {
                            _pe.Position += 16;
                            ulong pointer = _pe.MapVirtualAddressToRaw(_pe.ReadUInt64());
                            if (CheckPointerInDataSection(pointer))
                            {
                                var pointers = _pe.ReadClassArray<ulong>((long) pointer, maxMetadataUsages);
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