using System;
using System.Text;

namespace LibCpp2IL.MachO
{
    public class MachOSegmentCommand64 : ReadableClass
    {
        public string SegmentName; // 16 bytes
        public ulong VirtualAddress;
        public ulong VirtualSize;
        public ulong FileOffset;
        public ulong FileSize;
        public uint MaxProtection;
        public uint InitialProtection;
        public uint NumSections;
        public uint Flags;
        
        public MachOSection64[] Sections = Array.Empty<MachOSection64>();

        public override void Read(ClassReadingBinaryReader reader)
        {
            SegmentName = Encoding.UTF8.GetString(reader.ReadByteArrayAtRawAddressNoLock(-1, 16)).TrimEnd('\0');
            VirtualAddress = reader.ReadUInt64();
            VirtualSize = reader.ReadUInt64();
            FileOffset = reader.ReadUInt64();
            FileSize = reader.ReadUInt64();
            MaxProtection = reader.ReadUInt32();
            InitialProtection = reader.ReadUInt32();
            NumSections = reader.ReadUInt32();
            Flags = reader.ReadUInt32();

            Sections = new MachOSection64[NumSections];
            for (var i = 0; i < NumSections; i++)
            {
                Sections[i] = reader.ReadReadableHereNoLock<MachOSection64>();
            }
        }
    }
}