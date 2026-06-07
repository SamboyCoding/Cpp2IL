using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;

namespace LibCpp2IL;

public static class BinaryReaderHelpers
{
    public static ushort ReadUInt16WithReversedBits(this BinaryReader binRdr)
    {
        return BinaryPrimitives.ReverseEndianness(binRdr.ReadUInt16());
    }

    public static short ReadInt16WithReversedBits(this BinaryReader binRdr)
    {
        return BinaryPrimitives.ReverseEndianness(binRdr.ReadInt16());
    }

    public static uint ReadUInt32WithReversedBits(this BinaryReader binRdr)
    {
        return BinaryPrimitives.ReverseEndianness(binRdr.ReadUInt32());
    }

    public static int ReadInt32WithReversedBits(this BinaryReader binRdr)
    {
        return BinaryPrimitives.ReverseEndianness(binRdr.ReadInt32());
    }

    public static ulong ReadUInt64WithReversedBits(this BinaryReader binRdr)
    {
        return BinaryPrimitives.ReverseEndianness(binRdr.ReadUInt64());
    }

    public static long ReadInt64WithReversedBits(this BinaryReader binRdr)
    {
        return BinaryPrimitives.ReverseEndianness(binRdr.ReadInt64());
    }

    public static float ReadSingleWithReversedBits(this BinaryReader binRdr)
    {
        return BitCast<uint, float>(binRdr.ReadUInt32WithReversedBits());
    }

    public static double ReadDoubleWithReversedBits(this BinaryReader binRdr)
    {
        return BitCast<ulong, double>(binRdr.ReadUInt64WithReversedBits());
    }

    private static TTo BitCast<TFrom, TTo>(TFrom source)
        where TFrom : unmanaged
        where TTo : unmanaged
    {
#if NET8_0_OR_GREATER
        return Unsafe.BitCast<TFrom, TTo>(source);
#else
        return Unsafe.ReadUnaligned<TTo>(ref Unsafe.As<TFrom, byte>(ref source));
#endif
    }
}
