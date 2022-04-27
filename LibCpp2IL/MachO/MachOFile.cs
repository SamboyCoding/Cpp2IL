using System.IO;
using System.Linq;
using LibCpp2IL.Logging;

namespace LibCpp2IL.MachO
{
    public class MachOFile : Il2CppBinary
    {
        private byte[] _raw;
        
        private readonly MachOHeader _header;
        private readonly MachOLoadCommand[] _loadCommands;

        private readonly MachOSegmentCommand[] Segments64;
        private readonly MachOSection[] Sections64;
        
        public MachOFile(MemoryStream input) : base(input)
        {
            _raw = input.GetBuffer();
            
            LibLogger.Verbose("\tReading Mach-O header...");
            _header = ReadReadable<MachOHeader>();

            switch (_header.Magic)
            {
                case MachOHeader.MAGIC_32_BIT:
                    LibLogger.Verbose("Mach-O is 32-bit...");
                    is32Bit = true;
                    break;
                case MachOHeader.MAGIC_64_BIT:
                    LibLogger.Verbose("Mach-O is 64-bit...");
                    is32Bit = false;
                    break;
                default:
                    throw new($"Unknown Mach-O Magic: {_header.Magic}");
            }

            switch (_header.CpuType)
            {
                case MachOCpuType.CPU_TYPE_I386:
                    LibLogger.VerboseNewline("Mach-O contains x86_32 instructions.");
                    InstructionSetId = DefaultInstructionSets.X86_32;
                    break;
                case MachOCpuType.CPU_TYPE_X86_64:
                    LibLogger.VerboseNewline("Mach-O contains x86_64 instructions.");
                    InstructionSetId = DefaultInstructionSets.X86_64;
                    break;
                case MachOCpuType.CPU_TYPE_ARM:
                    LibLogger.VerboseNewline("Mach-O contains ARM (32-bit) instructions.");
                    InstructionSetId = DefaultInstructionSets.ARM_V7;
                    break;
                case MachOCpuType.CPU_TYPE_ARM64:
                    LibLogger.VerboseNewline("Mach-O contains ARM64 instructions.");
                    InstructionSetId = DefaultInstructionSets.ARM_V8;
                    break;
                default:
                    throw new($"Don't know how to handle a Mach-O CPU Type of {_header.CpuType}");
            }
            
            if(_header.Magic == MachOHeader.MAGIC_32_BIT)
                LibLogger.ErrorNewline("32-bit MACH-O files have not been tested! Please report any issues.");
            else
                LibLogger.WarnNewline("Mach-O Support is experimental. Please open an issue if anything seems incorrect.");

            LibLogger.Verbose("\tReading Mach-O load commands...");
            _loadCommands = ReadReadableArrayAtRawAddr<MachOLoadCommand>(-1, _header.NumLoadCommands);
            LibLogger.VerboseNewline($"Read {_loadCommands.Length} load commands.");
            
            Segments64 = _loadCommands.Where(c => c.Command == LoadCommandId.LC_SEGMENT_64).Select(c => c.CommandData).Cast<MachOSegmentCommand>().ToArray();
            Sections64 = Segments64.SelectMany(s => s.Sections).ToArray();
            
            LibLogger.VerboseNewline($"\tMach-O contains {Segments64.Length} segments, split into {Sections64.Length} sections.");
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

        private MachOSection GetTextSection64()
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