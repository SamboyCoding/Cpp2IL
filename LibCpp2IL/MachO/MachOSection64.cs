using System.Text;

namespace LibCpp2IL.MachO
{
    public class MachOSection64 : ReadableClass
    {
        public string SectionName; // 16 bytes
        public string SegmentName; // 16 bytes
        public ulong Address;
        public ulong Size;
        public uint Offset;
        public uint Alignment;
        public uint RelocationOffset;
        public uint NumberOfRelocations;
        public uint Flags;
        
        public uint Reserved1;
        public uint Reserved2;
        public uint Reserved3;
        
        public override void Read(ClassReadingBinaryReader reader)
        {
            SectionName = Encoding.UTF8.GetString(reader.ReadByteArrayAtRawAddressNoLock(-1, 16)).TrimEnd('\0');
            SegmentName = Encoding.UTF8.GetString(reader.ReadByteArrayAtRawAddressNoLock(-1, 16)).TrimEnd('\0');
            Address = reader.ReadUInt64();
            Size = reader.ReadUInt64();
            Offset = reader.ReadUInt32();
            Alignment = reader.ReadUInt32();
            RelocationOffset = reader.ReadUInt32();
            NumberOfRelocations = reader.ReadUInt32();
            Flags = reader.ReadUInt32();
            
            Reserved1 = reader.ReadUInt32();
            Reserved2 = reader.ReadUInt32();
            Reserved3 = reader.ReadUInt32();
        }
    }
}