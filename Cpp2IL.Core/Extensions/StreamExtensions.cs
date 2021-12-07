using System;
using System.IO;

namespace Cpp2IL.Core.Extensions;

public static class StreamExtensions
{
    public static uint ReadUnityCompressedUint(this Stream stream)
    {
        var b = stream.ReadByte();
        if (b < 128)
            return (uint) b;
        if (b == 240)
        {
            //Full Uint
            var buffer = new byte[4];
            stream.Read(buffer, 0, 4);
            return BitConverter.ToUInt32(buffer, 0);
        }

        //Special constant values
        if (b == byte.MaxValue)
            return uint.MaxValue;
        if (b == 254)
            return uint.MaxValue - 1;

        if ((b & 192) == 192)
        {
            //3 more to read
            return (uint) ((b & ~192U) << 24 | (uint) (stream.ReadByte() << 16) | (uint) (stream.ReadByte() << 8) | (uint) stream.ReadByte());
        }

        if ((b & 128) == 128)
        {
            //1 more to read
            return (uint) ((b & ~128U) << 8 | (uint) stream.ReadByte());
        }


        throw new Exception($"How did we even get here? Invalid compressed int first byte {b}");
    }

    public static int ReadUnityCompressedInt(this Stream stream)
    {
        //Ref libil2cpp, il2cpp\utils\ReadCompressedInt32
        var unsigned = stream.ReadUnityCompressedUint();

        if (unsigned == uint.MaxValue)
            return int.MinValue;

        var isNegative = (unsigned & 1) == 1;
        unsigned >>= 1;
        if (isNegative)
            return -(int) (unsigned + 1);

        return (int) unsigned;
    }
}