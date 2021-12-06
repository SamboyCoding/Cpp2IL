namespace LibCpp2IL.Metadata
{
    public class Il2CppCustomAttributeTypeRange : IIl2CppTokenProvider
    {
        [Version(Min = 24.1f)] public uint token;
        public int start;
        [Version(Max = 27.1f)] public int count;
        
        public uint Token => token;
    }
}