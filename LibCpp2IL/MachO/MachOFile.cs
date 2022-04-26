using System.IO;
using System.Linq;

namespace LibCpp2IL.MachO
{
    public class MachOFile : Il2CppBinary
    {
        private byte[] _raw;
        
        private readonly MachOHeader _header;
        private readonly MachOLoadCommand[] _loadCommands;

        private readonly MachOSegmentCommand64[] Segments64;
        private readonly MachOSection64[] Sections64;
        
        public MachOFile(MemoryStream input) : base(input)
        {
            _raw = input.GetBuffer();
            _header = ReadReadable<MachOHeader>();
            
            InstructionSetId = _header.Magic == MachOHeader.MAGIC_32_BIT ? DefaultInstructionSets.ARM_V7 : DefaultInstructionSets.ARM_V8;
            is32Bit = InstructionSetId == DefaultInstructionSets.ARM_V7;
            
            if(_header.Magic == MachOHeader.MAGIC_32_BIT)
                throw new("32-bit MACH-O files are not supported yet");

            _loadCommands = ReadReadableArrayAtRawAddr<MachOLoadCommand>(-1, _header.NumLoadCommands);
            
            Segments64 = _loadCommands.Where(c => c.Command == LoadCommandId.LC_SEGMENT_64).Select(c => c.CommandData).Cast<MachOSegmentCommand64>().ToArray();
            Sections64 = Segments64.SelectMany(s => s.Sections).ToArray();
        }

        public override long RawLength => _raw.Length;
        public override byte GetByteAtRawAddress(ulong addr) => _raw[addr];

        public override long MapVirtualAddressToRaw(ulong uiAddr)
        {
            var sec = Sections64.FirstOrDefault(s => s.Address <= uiAddr && uiAddr < s.Address + s.Size);
            
            if (sec == null)
                throw new($"Could not find section for virtual address 0x{uiAddr:X}. Lowest section address is 0x{Sections64.Min(s => s.Address):X}, highest section address is 0x{Sections64.Max(s => s.Address + s.Size):X}");

            return (long) (sec.Offset + (uiAddr - sec.Address));
        }

        public override ulong MapRawAddressToVirtual(uint offset)
        {
            var sec = Sections64.FirstOrDefault(s => s.Offset <= offset && offset < s.Offset + s.Size);
            
            if (sec == null)
                throw new($"Could not find section for raw address 0x{offset:X}");
            
            return sec.Address + (offset - sec.Offset);
        }

        public override ulong GetRva(ulong pointer)
        {
            return pointer; //TODO?
        }

        public override byte[] GetRawBinaryContent() => _raw;

        public override ulong GetVirtualAddressOfExportedFunctionByName(string toFind)
        {
            return 0; //TODO?
        }

        private MachOSection64 GetTextSection64()
        {
            var textSection = Sections64.FirstOrDefault(s => s.SectionName == "__text");
            
            if (textSection == null)
                throw new("Could not find __text section");

            return textSection;
        }

        public override byte[] GetEntirePrimaryExecutableSection()
        {
            var textSection = GetTextSection64();

            return _raw.SubArray((int) textSection.Offset, (int) textSection.Size);
        }

        public override ulong GetVirtualAddressOfPrimaryExecutableSection() => GetTextSection64().Address;
    }
}