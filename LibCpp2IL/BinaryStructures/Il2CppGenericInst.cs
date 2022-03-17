using System.Linq;

namespace LibCpp2IL.BinaryStructures
{
    public class Il2CppGenericInst
    {
        public ulong pointerCount;
        public ulong pointerStart;
        
        public ulong[] Pointers => LibCpp2IlMain.Binary!.GetPointers(pointerStart, (long) pointerCount);

        public Il2CppType[] Types => Pointers.Select(LibCpp2IlMain.Binary!.GetIl2CppTypeFromPointer).ToArray();
    }
}