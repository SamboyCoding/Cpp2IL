namespace LibCpp2IL.Metadata
{
    public class Il2CppCustomAttributeDataRange : IIl2CppTokenProvider
    {
        //Since v29
        public uint token;
        public uint startOffset;

        public uint Token => token;
    }
}