using System;
using LibCpp2IL;

namespace Cpp2IL.Core;

public readonly struct BinarySlice
{
    public static readonly BinarySlice Empty = new([]);

    public readonly int Length;

    private readonly byte[]? _computed;

    private readonly Il2CppBinary? _binary;
    private readonly int _offset;

    public BinarySlice(byte[] computed)
    {
        _computed = computed;
        Length = computed.Length;
    }

    public BinarySlice(Il2CppBinary binary, int offset, int length)
    {
        _binary = binary;
        _offset = offset;
        Length = length;
    }

    public byte[] ToArray()
    {
        if (_computed is not null)
            return _computed;

        return _binary!.GetRawBinaryContent()
                       .Slice(_offset, Length)
                       .ToArray();
    }

    public ReadOnlySpan<byte> AsSpan()
    {
        if (_computed is not null)
            return _computed;

        return _binary!.GetRawBinaryContent()
                       .Slice(_offset, Length);
    }
}
