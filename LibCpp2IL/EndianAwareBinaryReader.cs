using System;
using System.IO;
using System.Text;

namespace LibCpp2IL;

public class EndianAwareBinaryReader : BinaryReader
{
    protected bool ShouldReverseArrays = !BitConverter.IsLittleEndian; //Default to LE mode, so on LE systems, don't invert.

    public bool IsBigEndian { get; private set; } = false;

    private int _numBytesReadSinceLastCall = 0;

    public EndianAwareBinaryReader(Stream input) : base(input)
    {
    }

    public EndianAwareBinaryReader(Stream input, Encoding encoding) : base(input, encoding)
    {
    }

    public EndianAwareBinaryReader(Stream input, Encoding encoding, bool leaveOpen) : base(input, encoding, leaveOpen)
    {
    }

    public void SetBigEndian()
    {
        ShouldReverseArrays = BitConverter.IsLittleEndian; //Set to BE mode, so on LE systems we invert.
        IsBigEndian = true;
    }

    public override bool ReadBoolean()
    {
        _numBytesReadSinceLastCall += 1;

        return base.ReadBoolean();
    }

    public sealed override byte ReadByte()
    {
        _numBytesReadSinceLastCall += 1;

        return base.ReadByte();
    }

    public sealed override byte[] ReadBytes(int count)
    {
        _numBytesReadSinceLastCall += count;

        return base.ReadBytes(count);
    }

    public sealed override char ReadChar()
    {
        _numBytesReadSinceLastCall += 2;

        return base.ReadChar();
    }

    public sealed override char[] ReadChars(int count)
    {
        _numBytesReadSinceLastCall += 2 * count;

        return base.ReadChars(count);
    }

    public sealed override short ReadInt16()
    {
        _numBytesReadSinceLastCall += 2;

        if (!ShouldReverseArrays)
            return base.ReadInt16();

        return this.ReadInt16WithReversedBits();
    }

    public sealed override int ReadInt32()
    {
        _numBytesReadSinceLastCall += 4;

        if (!ShouldReverseArrays)
            return base.ReadInt32();

        return this.ReadInt32WithReversedBits();
    }

    public sealed override long ReadInt64()
    {
        _numBytesReadSinceLastCall += 8;

        if (!ShouldReverseArrays)
            return base.ReadInt64();

        return this.ReadInt64WithReversedBits();
    }

    public sealed override ushort ReadUInt16()
    {
        _numBytesReadSinceLastCall += 2;

        if (!ShouldReverseArrays)
            return base.ReadUInt16();

        return this.ReadUInt16WithReversedBits();
    }

    public sealed override uint ReadUInt32()
    {
        _numBytesReadSinceLastCall += 4;

        if (!ShouldReverseArrays)
            return base.ReadUInt32();

        return this.ReadUInt32WithReversedBits();
    }

    public sealed override ulong ReadUInt64()
    {
        _numBytesReadSinceLastCall += 8;

        if (!ShouldReverseArrays)
            return base.ReadUInt64();

        return this.ReadUInt64WithReversedBits();
    }

    public sealed override float ReadSingle()
    {
        _numBytesReadSinceLastCall += 4;

        if (!ShouldReverseArrays)
            return base.ReadSingle();

        return this.ReadSingleWithReversedBits();
    }

    public sealed override double ReadDouble()
    {
        _numBytesReadSinceLastCall += 8;

        if (!ShouldReverseArrays)
            return base.ReadDouble();

        return this.ReadDoubleWithReversedBits();
    }

    protected int GetNumBytesReadSinceLastCallAndClear()
    {
        var numBytesRead = _numBytesReadSinceLastCall;
        _numBytesReadSinceLastCall = 0;
        return numBytesRead;
    }
}
