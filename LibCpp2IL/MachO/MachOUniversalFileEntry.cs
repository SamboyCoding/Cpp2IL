namespace LibCpp2IL.MachO;

public class MachOUniversalFileEntry : ReadableClass
{
    public MachOCpuType CpuType; //cpu specifier of MachOFile
    public MachOCpuSubtype CpuSubtype; //cpu specifier of MachOFile
    public ulong FileOffset; // offset of MachOFile in universal container
    public ulong FileSize; // size of MachOFile
    public uint FileAlignment; // alignment of MachOFile

    public override void Read(ClassReadingBinaryReader reader)
    {
        CpuType = (MachOCpuType)reader.ReadUInt32WithReversedBits();
        CpuSubtype = (MachOCpuSubtype)reader.ReadUInt32WithReversedBits();
        FileOffset = reader.ReadUInt32WithReversedBits();
        FileSize = reader.ReadUInt32WithReversedBits();
        FileAlignment = reader.ReadUInt32WithReversedBits();
    }
}
