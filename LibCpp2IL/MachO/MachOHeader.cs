namespace LibCpp2IL.MachO;

public class MachOHeader : ReadableClass
{
    public const uint MAGIC_32_BIT = 0xFEEDFACE;
    public const uint MAGIC_64_BIT = 0xFEEDFACF;

    public uint Magic; //0xFEEDFACE for 32-bit, 0xFEEDFACF for 64-bit
    public MachOCpuType CpuType; //cpu specifier
    public MachOCpuSubtype CpuSubtype; //cpu specifier
    public MachOFileType FileType; //type of file
    public uint NumLoadCommands; //number of load commands
    public uint SizeOfLoadCommands; //size of load commands
    public MachOHeaderFlags Flags; //flags

    public uint Reserved; //Only on 64-bit

    public override void Read(ClassReadingBinaryReader reader)
    {
        Magic = reader.ReadUInt32();
        CpuType = (MachOCpuType)reader.ReadUInt32();
        CpuSubtype = (MachOCpuSubtype)reader.ReadUInt32();
        FileType = (MachOFileType)reader.ReadUInt32();
        NumLoadCommands = reader.ReadUInt32();
        SizeOfLoadCommands = reader.ReadUInt32();
        Flags = (MachOHeaderFlags)reader.ReadUInt32();

        if (Magic == MAGIC_64_BIT)
            Reserved = reader.ReadUInt32();
    }
}
