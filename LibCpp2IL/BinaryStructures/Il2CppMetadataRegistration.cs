namespace LibCpp2IL.BinaryStructures;

public class Il2CppMetadataRegistration : ReadableClass
{
    private const int NumPointerFields = 9;
    private const int NumIntFields = 7;

    /*
     * IMPORTANT:
     * TECHNICALLY all of the counts are defined as int32_t (except for metadataUsagesCount which is size_t for some reason).
     * However, this struct is aligned/packed to the native pointer width, so we actually just treat these as a bunch of ulongs (i.e. pointers/native-width uints), and the padding (4*00 bytes after each count) is ignored.
     * This is (probably?) a little bit more performant than aligning (i.e. setting the stream position, i.e. seeking) after reading each count.
     * Future: Does this cause issues with little-endian vs big-endian? I've not actually come across a big-endian system - or binary - to test this on.
     *
     * Regardless of how we do it, the fact of the matter is that the count fields are (as well as the pointers which of course are) in practice [pointer size] bytes before the next field, not always 4,
     * so when calculating the total size of this struct, we need to take that into account.
     */
    public static int GetStructSize(bool isBinary32Bit)
        => (NumIntFields + NumPointerFields) * (isBinary32Bit ? sizeof(int) : sizeof(long)); //On 32-bit platforms, all pointers (represented in fields by long/ulong) are 32-bit. If this struct is updated, update the number of fields above.

    public long genericClassesCount;
    public ulong genericClasses;
    public long genericInstsCount;
    public ulong genericInsts;
    public long genericMethodTableCount;
    public ulong genericMethodTable;
    public long numTypes;
    public ulong typeAddressListAddress;
    public long methodSpecsCount;
    public ulong methodSpecs;

    public long fieldOffsetsCount;
    public ulong fieldOffsetListAddress;

    public long typeDefinitionsSizesCount;
    public ulong typeDefinitionsSizes;
    public ulong metadataUsagesCount; //this one, and only this one, is defined as size_t. The rest of the counts are int32_t.
    public ulong metadataUsages;

    public override void Read(ClassReadingBinaryReader reader)
    {
        //All of the count fields (barring metadataUsagesCount) are 32-bit ints. However this struct is aligned to the size of the pointers, so we actually just read as nuint
        genericClassesCount = reader.ReadNInt();
        genericClasses = reader.ReadNUint();
        genericInstsCount = reader.ReadNInt();
        genericInsts = reader.ReadNUint();
        genericMethodTableCount = reader.ReadNInt();
        genericMethodTable = reader.ReadNUint();
        numTypes = reader.ReadNInt();
        typeAddressListAddress = reader.ReadNUint();
        methodSpecsCount = reader.ReadNInt();
        methodSpecs = reader.ReadNUint();

        fieldOffsetsCount = reader.ReadNInt();
        fieldOffsetListAddress = reader.ReadNUint();

        typeDefinitionsSizesCount = reader.ReadNInt();
        typeDefinitionsSizes = reader.ReadNUint();
        metadataUsagesCount = reader.ReadNUint();
        metadataUsages = reader.ReadNUint();
    }
}
