namespace LibCpp2IL.PE;

public class FileHeader : ReadableClass
{
    public ushort Machine;
    public ushort NumberOfSections;
    public uint TimeDateStamp;
    public uint PointerToSymbolTable;
    public uint NumberOfSymbols;
    public ushort SizeOfOptionalHeader;
    public ushort Characteristics;

    public override void Read(ClassReadingBinaryReader reader)
    {
        Machine = reader.ReadUInt16();
        NumberOfSections = reader.ReadUInt16();
        TimeDateStamp = reader.ReadUInt32();
        PointerToSymbolTable = reader.ReadUInt32();
        NumberOfSymbols = reader.ReadUInt32();
        SizeOfOptionalHeader = reader.ReadUInt16();
        Characteristics = reader.ReadUInt16();
    }
}
