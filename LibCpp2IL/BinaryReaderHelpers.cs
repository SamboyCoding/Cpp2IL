using System;
using System.IO;

namespace LibCpp2IL;

public static class BinaryReaderHelpers
{
    public static byte[] Reverse(this byte[] b)
    {
        Array.Reverse(b);
        return b;
    }

    public static ushort ReadUInt16WithReversedBits(this BinaryReader binRdr)
    {
        return BitConverter.ToUInt16(binRdr.ReadBytesRequired(sizeof(ushort)).Reverse(), 0);
    }

    public static short ReadInt16WithReversedBits(this BinaryReader binRdr)
    {
        return BitConverter.ToInt16(binRdr.ReadBytesRequired(sizeof(short)).Reverse(), 0);
    }

    public static uint ReadUInt32WithReversedBits(this BinaryReader binRdr)
    {
        return BitConverter.ToUInt32(binRdr.ReadBytesRequired(sizeof(uint)).Reverse(), 0);
    }

    public static int ReadInt32WithReversedBits(this BinaryReader binRdr)
    {
        return BitConverter.ToInt32(binRdr.ReadBytesRequired(sizeof(int)).Reverse(), 0);
    }

    public static ulong ReadUInt64WithReversedBits(this BinaryReader binRdr)
    {
        return BitConverter.ToUInt64(binRdr.ReadBytesRequired(sizeof(ulong)).Reverse(), 0);
    }

    public static long ReadInt64WithReversedBits(this BinaryReader binRdr)
    {
        return BitConverter.ToInt64(binRdr.ReadBytesRequired(sizeof(long)).Reverse(), 0);
    }

    public static float ReadSingleWithReversedBits(this BinaryReader binRdr)
    {
        return BitConverter.ToSingle(binRdr.ReadBytesRequired(sizeof(float)).Reverse(), 0);
    }

    public static double ReadDoubleWithReversedBits(this BinaryReader binRdr)
    {
        return BitConverter.ToDouble(binRdr.ReadBytesRequired(sizeof(double)).Reverse(), 0);
    }

    private static byte[] ReadBytesRequired(this BinaryReader binRdr, int byteCount)
    {
        var result = binRdr.ReadBytes(byteCount);

        if (result.Length != byteCount)
            throw new EndOfStreamException($"{byteCount} bytes required from stream, but only {result.Length} returned.");

        return result;
    }
}
