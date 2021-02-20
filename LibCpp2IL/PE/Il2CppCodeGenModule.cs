namespace LibCpp2IL.PE
{
    public class Il2CppCodeGenModule
    {
        public ulong moduleName; //pointer
        public long methodPointerCount; //ulong
        public ulong methodPointers; //pointer

        [Version(Min = 27.1f)] public long adjustorThunkCount;
        [Version(Min = 27.1f)] public ulong adjustorThunks; //Pointer
        
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
                    _cachedName = LibCpp2IlMain.ThePe!.ReadStringToNull(LibCpp2IlMain.ThePe.MapVirtualAddressToRaw(moduleName));

                return _cachedName!;
            }
        }
    }
}