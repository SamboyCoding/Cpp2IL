namespace LibCpp2IL.Elf
{
    public class ElfFileIdent
    {
        public int Magic;
        public byte Architecture; //1 => 32-bit, 2 => 64-bit
        public byte Endianness; //1 => LE, 2 => BE
        public byte Version; //Must be 1
        public byte OSAbi; //Probably ignore.
        public byte AbiVersion;
        //7 bytes of padding here.
    }
}