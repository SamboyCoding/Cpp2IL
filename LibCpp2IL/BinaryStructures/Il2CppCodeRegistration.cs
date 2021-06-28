namespace LibCpp2IL.BinaryStructures
{
    
        public class Il2CppCodeRegistration
        {
            [Version(Max = 24.1f)] public ulong methodPointersCount;
            [Version(Max = 24.1f)] public ulong methodPointers;
            
            public ulong reversePInvokeWrapperCount;
            public ulong reversePInvokeWrappers;
            
            public ulong genericMethodPointersCount;
            public ulong genericMethodPointers;
            
            //Present in v27.1 and v24.5, but not v27.0
            [Version(Min = 27.1f)]
            [Version(Min = 24.5f, Max = 24.5f)]
            public ulong genericAdjustorThunks;
            
            public ulong invokerPointersCount;
            public ulong invokerPointers;
            
            [Version(Max = 24.5f)] //Removed in v27
            public long customAttributeCount;
            [Version(Max = 24.5f)] //Removed in v27
            public ulong customAttributeGeneratorListAddress;
            
            public ulong unresolvedVirtualCallCount;
            public ulong unresolvedVirtualCallPointers;
            
            public ulong interopDataCount;
            public ulong interopData;
            
            [Version(Min = 24.3f)]
            public ulong windowsRuntimeFactoryCount;
            [Version(Min = 24.3f)]
            public ulong windowsRuntimeFactoryTable;
            
            [Version(Min = 24.2f)] public ulong codeGenModulesCount;
            [Version(Min = 24.2f)] public ulong addrCodeGenModulePtrs;
        }
}