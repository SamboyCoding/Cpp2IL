namespace LibCpp2IL.Elf;

/// <summary>
/// Not an actual Elf struct, but an internal representation for a bunch of them.
/// </summary>
public class ElfRelocation
{
    public ElfRelocationType Type;
    public ulong Offset;
    public ulong? Addend;
    public ulong pRelatedSymbolTable;
    public ulong IndexInSymbolTable;

    private static ulong GetTypeBitsFromInfo(ulong info, ElfFile f) => f.is32Bit ? info & 0xFF : info & 0xFFFF_FFFF;

    private static ulong GetSymBitsFromInfo(ulong info, ElfFile f) => f.is32Bit ? info >> 8 : info >> 32;

    public ElfRelocation(ElfFile f, ElfRelEntry relocation, ulong tablePointer)
    {
        Offset = relocation.Offset;
        Addend = null;
        Type = (ElfRelocationType)GetTypeBitsFromInfo(relocation.Info, f);
        IndexInSymbolTable = GetSymBitsFromInfo(relocation.Info, f);
        pRelatedSymbolTable = tablePointer;
    }

    public ElfRelocation(ElfFile f, ElfRelaEntry relocation, ulong tablePointer)
    {
        Offset = relocation.Offset;
        Addend = relocation.Addend; //Same as the above ctor except for this.
        Type = (ElfRelocationType)GetTypeBitsFromInfo(relocation.Info, f);
        IndexInSymbolTable = GetSymBitsFromInfo(relocation.Info, f);
        pRelatedSymbolTable = tablePointer;
    }

    protected bool Equals(ElfRelocation other)
    {
        return Offset == other.Offset;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((ElfRelocation)obj);
    }

    public override int GetHashCode()
    {
        return Offset.GetHashCode();
    }

    public static bool operator ==(ElfRelocation? left, ElfRelocation? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(ElfRelocation? left, ElfRelocation? right)
    {
        return !Equals(left, right);
    }
}
