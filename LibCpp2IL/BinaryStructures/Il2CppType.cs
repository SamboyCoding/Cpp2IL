using System;
using LibCpp2IL.Metadata;

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
            attrs = bits & 0b1111_1111_1111_1111;
            type = (Il2CppTypeEnum) ((bits >> 16) & 0b1111_1111);
            
            //Note for future: some unity 2021 version (2021.1.0?) changed this to be 5 bits not 6
            //Which shifts num_mods, byref, and pinned left one
            //And adds a new bit 31 which is valuetype
            num_mods = (bits >> 24) & 0b11_1111;
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

        public Il2CppTypeDefinition AsClass()
        {
            if(type != Il2CppTypeEnum.IL2CPP_TYPE_CLASS)
                throw new Exception("Type is not a class");

            return LibCpp2IlMain.TheMetadata!.typeDefs[data.classIndex];
        }
    }
}