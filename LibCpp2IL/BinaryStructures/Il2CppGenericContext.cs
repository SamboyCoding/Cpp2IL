namespace LibCpp2IL.BinaryStructures;

public class Il2CppGenericContext : ReadableClass
{
    /* The instantiation corresponding to the class generic parameters */
    public ulong class_inst;

    /* The instantiation corresponding to the method generic parameters */
    public ulong method_inst;

    public Il2CppGenericInst? ClassInst
    {
        get
        {
            if (class_inst == 0) return null;
            return OwningContext.Binary.ReadReadableAtVirtualAddress<Il2CppGenericInst>(class_inst);
        }
    }

    public Il2CppGenericInst? MethodInst
    {
        get
        {
            if (method_inst == 0) return null;
            return OwningContext.Binary.ReadReadableAtVirtualAddress<Il2CppGenericInst>(method_inst);
        }
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        class_inst = reader.ReadNUint();
        method_inst = reader.ReadNUint();
    }
}
