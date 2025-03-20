namespace LibCpp2IL.MachO;

public class MachOExportEntry(string name, ulong address, ulong flags, ulong other, string? importName)
{
    public string Name = name;
    public ulong Address = address;
    public ulong Flags = flags;
    public ulong Other = other;
    public string? ImportName = importName;
}
