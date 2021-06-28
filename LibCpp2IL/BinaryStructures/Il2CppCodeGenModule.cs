namespace LibCpp2IL.BinaryStructures
{
    public class Il2CppCodeGenModule
    {
        public ulong moduleName; //pointer
        public long methodPointerCount; //ulong
        public ulong methodPointers; //pointer

        //Present in v27.1 and v24.5, but not v27.0
        [Version(Min = 27.1f)]
        [Version(Min = 24.5f, Max = 24.5f)] 
        public long adjustorThunkCount;
        [Version(Min = 27.1f)]
        [Version(Min = 24.5f, Max = 24.5f)]
        public ulong adjustorThunks; //Pointer
        
        public ulong invokerIndices; //ulong
        public ulong reversePInvokeWrapperCount; //ulong
        public ulong reversePInvokeWrapperIndices;
        
        public long rgctxRangesCount;
        public ulong pRgctxRanges;

        public long rgctxsCount;
        public ulong rgctxs;
        public ulong debuggerMetadata;

        [Version(Min = 27)] public ulong customAttributeCacheGenerator;
        [Version(Min = 27)] public ulong moduleInitializer;
        [Version(Min = 27)] public ulong staticConstructorTypeIndices;
        [Version(Min = 27)] public ulong metadataRegistration; //Per-assembly mode only
        [Version(Min = 27)] public ulong codeRegistration; //Per-assembly mode only.

        private string? _cachedName;
        public string Name
        {
            get
            {
                if(_cachedName == null)
                    _cachedName = LibCpp2IlMain.Binary!.ReadStringToNull(LibCpp2IlMain.Binary.MapVirtualAddressToRaw(moduleName));

                return _cachedName!;
            }
        }

        public Il2CppTokenRangePair[] RGCTXRanges => LibCpp2IlMain.Binary!.GetRGCTXRangePairsForModule(this);
    }
}