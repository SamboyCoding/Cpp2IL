namespace LibCpp2IL.BinaryStructures
{
    public class Il2CppMetadataRegistration : ReadableClass
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
        
        public override void Read(ClassReadingBinaryReader reader)
        {
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
}