using System.Collections.Generic;
using System.Linq;
using Cpp2IL.PE;

namespace Cpp2IL
{
    public class PlusSearch
    {
        private class Section
        {
            public ulong start;
            public ulong end;
            public ulong address;
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
                        start = section.PointerToRawData,
                        end = section.PointerToRawData + section.SizeOfRawData,
                        address = section.VirtualAddress + imageBase
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
                        start = section.PointerToRawData,
                        end = section.PointerToRawData + section.SizeOfRawData,
                        address = section.VirtualAddress + imageBase
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
                        start = section.VirtualAddress,
                        end = section.VirtualAddress + section.VirtualSize + imageBase,
                        address = section.VirtualAddress + imageBase
                    });
                }
            }
        }

        public ulong FindCodeRegistration()
        {
            foreach (var section in search)
            {
                _pe.Position = (long) section.start;
                while ((ulong)_pe.Position < section.end)
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
                                    return (ulong)addr - section.start + section.address; //VirtualAddress
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
            foreach (var section in search)
            {
                _pe.Position = (long) section.start;
                while ((ulong)_pe.Position < section.end)
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
                                    return (ulong)addr - section.start + section.address; //VirtualAddress
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
                _pe.Position = (long) section.start;
                while ((ulong)_pe.Position < section.end)
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
                                    return (ulong)addr - 48ul - section.start + section.address; //VirtualAddress
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
                _pe.Position = (long) section.start;
                while ((ulong)_pe.Position < section.end)
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
                                    return (ulong)addr - 96ul - section.start + section.address; //VirtualAddress
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
            return dataSections.Any(x => pointer >= x.start && pointer <= x.end);
        }

        private bool CheckAllInExecSection(IEnumerable<ulong> pointers)
        {
            return pointers.All(x => execSections.Any(y => x >= y.start && x <= y.end));
        }

        private bool CheckAllInExecSection(IEnumerable<uint> pointers)
        {
            return pointers.All(x => execSections.Any(y => x >= y.start && x <= y.end));
        }
    }
}