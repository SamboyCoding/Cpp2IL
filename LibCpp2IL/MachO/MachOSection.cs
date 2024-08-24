using System.Text;

namespace LibCpp2IL.MachO;

public class MachOSection : ReadableClass
{
    public string SectionName = "INVALID"; // 16 bytes
    public string ContainingSegmentName = "INVALID"; // 16 bytes

    public ulong Address;
    public ulong Size;

    public uint Offset;
    public uint Alignment;
    public uint RelocationOffset;
    public uint NumberOfRelocations;
    public MachOSectionFlags Flags;

    public uint Reserved1;
    public uint Reserved2;

    public uint Reserved3; //64-bit only

    public override void Read(ClassReadingBinaryReader reader)
    {
        SectionName = Encoding.UTF8.GetString(reader.ReadByteArrayAtRawAddressNoLock(-1, 16)).TrimEnd('\0');
        ContainingSegmentName = Encoding.UTF8.GetString(reader.ReadByteArrayAtRawAddressNoLock(-1, 16)).TrimEnd('\0');

        Address = reader.ReadNUint();
        Size = reader.ReadNUint();

        Offset = reader.ReadUInt32();
        Alignment = reader.ReadUInt32();
        RelocationOffset = reader.ReadUInt32();
        NumberOfRelocations = reader.ReadUInt32();
        Flags = (MachOSectionFlags)reader.ReadUInt32();

        Reserved1 = reader.ReadUInt32();
        Reserved2 = reader.ReadUInt32();

        if (!reader.is32Bit)
            Reserved3 = reader.ReadUInt32();
    }
}
