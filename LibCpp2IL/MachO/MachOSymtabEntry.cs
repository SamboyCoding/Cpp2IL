using System.Text;

namespace LibCpp2IL.MachO;

public class MachOSymtabEntry
{
    public uint NameOffset;
    public byte Type;
    public byte Section;
    public ushort Description;
    public ulong Value; // Architecture sized

    public string Name = null!; //Null-suppressed because: Initialized in Read

    public bool IsExternal => (Type & 0b1) == 0b1;
    public bool IsSymbolicDebugging => (Type & 0b1110_0000) != 0;
    public bool IsPrivateExternal => (Type & 0b0001_0000) == 0b0001_0000;

    private byte TypeBits => (byte)(Type & 0b1110);

    public bool IsTypeUndefined => Section == 0 && TypeBits == 0b0000;
    public bool IsTypeAbsolute => Section == 0 && TypeBits == 0b0010;
    public bool IsTypePreboundUndefined => Section == 0 && TypeBits == 0b1100;
    public bool IsTypeIndirect => Section == 0 && TypeBits == 0b1010;
    public bool IsTypeSection => TypeBits == 0b1110;

    public string GetTypeFlags()
    {
        var ret = new StringBuilder();

        if (IsExternal)
            ret.Append("EXTERNAL ");
        if (IsSymbolicDebugging)
            ret.Append("SYMBOLIC_DEBUGGING ");
        if (IsPrivateExternal)
            ret.Append("PRIVATE_EXTERNAL ");

        if (IsTypeUndefined)
            ret.Append("TYPE_UNDEFINED ");
        else if (IsTypeAbsolute)
            ret.Append("TYPE_ABSOLUTE ");
        else if (IsTypePreboundUndefined)
            ret.Append("TYPE_PREBOUND_UNDEFINED ");
        else if (IsTypeIndirect)
            ret.Append("TYPE_INDIRECT ");
        else if (IsTypeSection)
            ret.Append("TYPE_SECTION ");
        else
            ret.Append("TYPE_UNKNOWN ");

        return ret.ToString();
    }

    public void Read(ClassReadingBinaryReader reader, MachOSymtabCommand machOSymtabCommand)
    {
        NameOffset = reader.ReadUInt32();
        Type = reader.ReadByte();
        Section = reader.ReadByte();
        Description = reader.ReadUInt16();
        Value = reader.ReadNUint();

        var returnTo = reader.BaseStream.Position;
        Name = reader.ReadStringToNullNoLock(machOSymtabCommand.StringTableOffset + NameOffset);
        reader.BaseStream.Position = returnTo;
    }
}
