namespace LibCpp2IL.MachO;

public class MachOExportEntry(string name, long address, long flags, long other, string? importName)
{
    public string Name = name;
    public long Address = address;
    public long Flags = flags;
    public long Other = other;
    public string? ImportName = importName;
}
