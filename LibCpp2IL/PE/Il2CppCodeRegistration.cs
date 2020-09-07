namespace LibCpp2IL.PE
{
    
        public class Il2CppCodeRegistration
        {
            [Version(Max = 24.1f)] public ulong methodPointersCount;
            [Version(Max = 24.1f)] public ulong methodPointers;
            public ulong reversePInvokeWrapperCount;
            public ulong reversePInvokeWrappers;
            public ulong genericMethodPointersCount;
            public ulong genericMethodPointers;
            public ulong invokerPointersCount;
            public ulong invokerPointers;
            public long customAttributeCount;
            public ulong customAttributeGeneratorListAddress;
            public ulong unresolvedVirtualCallCount;
            public ulong unresolvedVirtualCallPointers;
            public ulong interopDataCount;
            public ulong interopData;
            [Version(Min = 24.2f)] public ulong codeGenModulesCount;
            [Version(Min = 24.2f)] public ulong addrCodeGenModulePtrs;
        }
}