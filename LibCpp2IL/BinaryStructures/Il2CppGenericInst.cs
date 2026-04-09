using System.Linq;

namespace LibCpp2IL.BinaryStructures;

public class Il2CppGenericInst : ReadableClass
{
    public ulong pointerCount;
    public ulong pointerStart;

    public ulong[] Pointers
    {
        get
        {
            var binary = OwningBinary;
            return binary == null ? [] : binary.ReadNUintArrayAtVirtualAddress(pointerStart, (long)pointerCount);
        }
    }

    public Il2CppType[] Types
    {
        get
        {
            var binary = OwningBinary;
            return binary == null ? [] : Pointers.Select(binary.GetIl2CppTypeFromPointer).ToArray();
        }
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        pointerCount = reader.ReadNUint();
        pointerStart = reader.ReadNUint();
    }
}
