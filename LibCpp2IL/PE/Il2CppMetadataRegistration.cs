namespace LibCpp2IL.PE
{
    public class Il2CppMetadataRegistration
    {
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
        public ulong metadataUsagesCount;
        public ulong metadataUsages;
    }
}