#pragma warning disable 8618
//Disable null check because this stuff is initialized by reflection
namespace LibCpp2IL.BinaryStructures
{
    public class Il2CppType
    {
        public ulong datapoint;
        public uint bits;
        public Union data { get; set; }
        public uint attrs { get; set; }
        public Il2CppTypeEnum type { get; set; }
        public uint num_mods { get; set; }
        public uint byref { get; set; }
        public uint pinned { get; set; }

        public void Init()
        {
            attrs = bits & 0xffff;
            type = (Il2CppTypeEnum) ((bits >> 16) & 0xff);
            num_mods = (bits >> 24) & 0x3f;
            byref = (bits >> 30) & 1;
            pinned = bits >> 31;
            data = new Union {dummy = datapoint};
        }

        public class Union
        {
            public ulong dummy;
            public long classIndex => (long) dummy;
            public ulong type => dummy;
            public ulong array => dummy;
            public long genericParameterIndex => (long) dummy;
            public ulong generic_class => dummy;
        }
    }
}