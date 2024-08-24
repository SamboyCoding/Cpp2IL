using System.Linq;

namespace LibCpp2IL.BinaryStructures;

public class Il2CppGenericInst : ReadableClass
{
    public ulong pointerCount;
    public ulong pointerStart;

    public ulong[] Pointers => LibCpp2IlMain.Binary!.ReadNUintArrayAtVirtualAddress(pointerStart, (long)pointerCount);

    public Il2CppType[] Types => Pointers.Select(LibCpp2IlMain.Binary!.GetIl2CppTypeFromPointer).ToArray();

    public override void Read(ClassReadingBinaryReader reader)
    {
        pointerCount = reader.ReadNUint();
        pointerStart = reader.ReadNUint();
    }
}
