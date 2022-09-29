namespace LibCpp2IL.BinaryStructures
{
    public class Il2CppCodeRegistration : ReadableClass
    {
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

        [Version(Min = 29.1f)] 
        public ulong unresolvedInstanceCallPointers;
        [Version(Min = 29.1f)] 
        public ulong unresolvedStaticCallPointers;

        [Version(Min = 23)]
        public ulong interopDataCount;
        [Version(Min = 23)]
        public ulong interopData;

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
}
