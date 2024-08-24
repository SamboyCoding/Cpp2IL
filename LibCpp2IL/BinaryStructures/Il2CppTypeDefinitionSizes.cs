namespace LibCpp2IL.BinaryStructures;

public class Il2CppTypeDefinitionSizes : ReadableClass
{
    public uint instance_size;
    public int native_size;
    public uint static_fields_size;
    public uint thread_static_fields_size;

    public override string ToString()
    {
        return $"Il2Cpp TypeDefinition Size Data {{InstanceSize={instance_size}, NativeSize={native_size}, StaticFieldsSize={static_fields_size}, Thread StaticFieldsSize={thread_static_fields_size}}}";
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        instance_size = reader.ReadUInt32();
        native_size = reader.ReadInt32();
        static_fields_size = reader.ReadUInt32();
        thread_static_fields_size = reader.ReadUInt32();
    }
}
