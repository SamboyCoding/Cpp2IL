namespace LibCpp2IL.MachO
{
    public class MachOExportEntry
    {
        public string Name;
        public long Address;
        public long Flags;
        public long Other;
        public string? ImportName;

        public MachOExportEntry(string name, long address, long flags, long other, string? importName)
        {
            Name = name;
            Address = address;
            Flags = flags;
            Other = other;
            ImportName = importName;
        }
    }
}