using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.IO.Compression;

namespace Cpp2IL.Core.Extensions;

public static class StreamExtensions
{
    public static uint ReadUnityCompressedUint(this Stream stream)
    {
        if (stream.Position == stream.Length)
            throw new EndOfStreamException();

        var b = stream.ReadByte();
        if (b < 128)
            return (uint)b;
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
            return (uint)((b & ~192U) << 24 | (uint)(stream.ReadByte() << 16) | (uint)(stream.ReadByte() << 8) | (uint)stream.ReadByte());
        }

        if ((b & 128) == 128)
        {
            //1 more to read
            return (uint)((b & ~128U) << 8 | (uint)stream.ReadByte());
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
            return -(int)(unsigned + 1);

        return (int)unsigned;
    }

    public static string ReadUnicodeString(this BinaryReader reader)
    {
        List<byte> bytes = [];
        var continueReading = true;
        var lastWasNull = false;
        while (continueReading)
        {
            var b = reader.ReadByte();

            if (b == 0 && lastWasNull)
                //Double null is a terminator for unicode strings
                continueReading = false;

            lastWasNull = b == 0;
            bytes.Add(b);
        }

        bytes.Add(reader.ReadByte()); //Last byte of null terminator will always be skipped - unskip it

        return Encoding.Unicode.GetString(bytes.ToArray()).TrimEnd('\0');
    }

    public static byte[] ReadBytes(this Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static byte[] ReadBytes(this ZipArchiveEntry entry) => entry.Open().ReadBytes();
}
