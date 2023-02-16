namespace LibCpp2IL.MachO
{
    public class MachOSymtabEntry
    {
        public uint NameOffset;
        public byte Type;
        public byte Section;
        public ushort Description;
        public ulong Value; // Architecture sized

        public string Name;
        
        public bool IsExternal => (Type & 0b1) == 0b1;
        public bool IsSymbolicDebugging => (Type & 0b1110_0000) != 0;
        public bool IsPrivateExternal => (Type & 0b0001_0000) == 0b0001_0000;
        
        private byte TypeBits => (byte) (Type & 0b1110);
        
        public bool IsTypeUndefined => Section == 0 && TypeBits == 0b0000;
        public bool IsTypeAbsolute => Section == 0 && TypeBits == 0b0010;
        public bool IsTypePreboundUndefined => Section == 0 && TypeBits == 0b1100;
        public bool IsTypeIndirect => Section == 0 && TypeBits == 0b1010;
        public bool IsTypeSection => TypeBits == 0b1110;

        public void Read(ClassReadingBinaryReader reader, MachOSymtabCommand machOSymtabCommand)
        {
            NameOffset = reader.ReadUInt32();
            Type = reader.ReadByte();
            Section = reader.ReadByte();
            Description = reader.ReadUInt16();
            Value = reader.ReadNUint();

            var returnTo = reader.BaseStream.Position;
            Name = reader.ReadStringToNull(machOSymtabCommand.StringTableOffset + NameOffset);
            reader.BaseStream.Position = returnTo;
        }
    }
}