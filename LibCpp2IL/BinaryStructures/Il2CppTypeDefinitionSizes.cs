namespace LibCpp2IL.BinaryStructures
{
    public class Il2CppTypeDefinitionSizes
    {
        public uint instance_size;
        public int native_size;
        public uint static_fields_size;
        public uint thread_static_fields_size;

        public override string ToString()
        {
            return $"Il2Cpp TypeDefinition Size Data {{InstanceSize={instance_size}, NativeSize={native_size}, StaticFieldsSize={static_fields_size}, Thread StaticFieldsSize={thread_static_fields_size}}}";
        }
    }
}