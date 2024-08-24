namespace LibCpp2IL.BinaryStructures;

public class Il2CppCodeRegistration : ReadableClass
{
    /*
     * IMPORTANT:
     * Again, like in Il2CppMetadataRegistration, all of the counts are defined as int32_t, but due to alignment, we treat them as native-width uints.
     * See the comment in Il2CppMetadataRegistration for more info.
     */
    public static int GetStructSize(bool isBinary32Bit, float metadataVersion)
    {
        //Unfortunately, this struct is not a fixed size, so we have to do some manual calculations.
        var size = 0;
        var ptrSize = isBinary32Bit ? 4 : 8;

        if (metadataVersion <= 24.15f)
            //methodPointers
            size += 2 * ptrSize;

        //reversePInvokeWrappers and genericMethodPointers
        size += 4 * ptrSize;

        if (metadataVersion is (>= 24.5f and < 27f) or >= 27.1f)
            //genericAdjustorThunks
            size += ptrSize;

        //invokerPointers
        size += 2 * ptrSize;

        if (metadataVersion <= 24.5f)
            //customAttributes
            size += 2 * ptrSize;

        //unresolvedVirtualCallPointers
        size += 2 * ptrSize;

        if (metadataVersion >= 29.1f)
            //unresolvedInstanceCallPointers and unresolvedStaticCallPointers
            size += 2 * ptrSize;

        if (metadataVersion >= 23f)
            //interopData
            size += 2 * ptrSize;

        if (metadataVersion >= 24.3f)
            //windowsRuntimeFactoryTable
            size += 2 * ptrSize;

        if (metadataVersion >= 24.2f)
            //addrCodeGenModulePtrs
            size += 2 * ptrSize;

        return size;
    }

    [Version(Max = 24.15f)] public ulong methodPointersCount;
    [Version(Max = 24.15f)] public ulong methodPointers;

    public ulong reversePInvokeWrapperCount;
    public ulong reversePInvokeWrappers;

    public ulong genericMethodPointersCount;
    public ulong genericMethodPointers;

    //Present in v27.1 and v24.5, but not v27.0
    [Version(Min = 27.1f)] [Version(Min = 24.5f, Max = 24.5f)]
    public ulong genericAdjustorThunks;

    public ulong invokerPointersCount;
    public ulong invokerPointers;

    [Version(Max = 24.5f)] //Removed in v27
    public ulong customAttributeCount;

    [Version(Max = 24.5f)] //Removed in v27
    public ulong customAttributeGeneratorListAddress;

    public ulong unresolvedVirtualCallCount; //Renamed to unresolvedIndirectCallCount in v29.1
    public ulong unresolvedVirtualCallPointers;

    [Version(Min = 29.1f)] public ulong unresolvedInstanceCallPointers;
    [Version(Min = 29.1f)] public ulong unresolvedStaticCallPointers;

    [Version(Min = 23)] public ulong interopDataCount;
    [Version(Min = 23)] public ulong interopData;

    [Version(Min = 24.3f)] public ulong windowsRuntimeFactoryCount;
    [Version(Min = 24.3f)] public ulong windowsRuntimeFactoryTable;

    [Version(Min = 24.2f)] public ulong codeGenModulesCount;
    [Version(Min = 24.2f)] public ulong addrCodeGenModulePtrs;

    public override void Read(ClassReadingBinaryReader reader)
    {
        if (IsAtMost(24.15f))
        {
            methodPointersCount = reader.ReadNUint();
            methodPointers = reader.ReadNUint();
        }

        reversePInvokeWrapperCount = reader.ReadNUint();
        reversePInvokeWrappers = reader.ReadNUint();

        genericMethodPointersCount = reader.ReadNUint();
        genericMethodPointers = reader.ReadNUint();

        if (IsAtLeast(24.5f) && IsNot(27f))
            genericAdjustorThunks = reader.ReadNUint();

        invokerPointersCount = reader.ReadNUint();
        invokerPointers = reader.ReadNUint();

        if (IsAtMost(24.5f))
        {
            customAttributeCount = reader.ReadNUint();
            customAttributeGeneratorListAddress = reader.ReadNUint();
        }

        unresolvedVirtualCallCount = reader.ReadNUint();
        unresolvedVirtualCallPointers = reader.ReadNUint();

        if (IsAtLeast(29.1f))
        {
            unresolvedInstanceCallPointers = reader.ReadNUint();
            unresolvedStaticCallPointers = reader.ReadNUint();
        }

        if (IsAtLeast(23f))
        {
            interopDataCount = reader.ReadNUint();
            interopData = reader.ReadNUint();
        }

        if (IsAtLeast(24.2f))
        {
            if (IsAtLeast(24.3f))
            {
                windowsRuntimeFactoryCount = reader.ReadNUint();
                windowsRuntimeFactoryTable = reader.ReadNUint();
            }

            codeGenModulesCount = reader.ReadNUint();
            addrCodeGenModulePtrs = reader.ReadNUint();
        }
    }
}
