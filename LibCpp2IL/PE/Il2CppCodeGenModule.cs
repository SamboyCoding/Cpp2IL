namespace LibCpp2IL.PE
{
    public class Il2CppCodeGenModule
    {
        public ulong moduleName; //pointer
        public long methodPointerCount; //ulong
        public ulong methodPointers; //pointer
        public ulong invokerIndices; //ulong
        public ulong reversePInvokeWrapperCount; //ulong
        public ulong reversePInvokeWrapperIndices;
        public ulong rgctxRangesCount;
        public ulong rgctxRanges;
        public ulong rgctxsCount;
        public ulong rgctxs;
        public ulong debuggerMetadata;

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