using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace LibCpp2IL;

public class ClassReadingBinaryReader : EndianAwareBinaryReader
{
    /// <summary>
    /// Set this to true to enable storing of amount of bytes read of each readable structure.
    /// </summary>
    public static bool EnableReadableSizeInformation = false;

    private SpinLock PositionShiftLock;

    public bool is32Bit;
    private MemoryStream? _memoryStream;

    public ulong PointerSize => is32Bit ? 4ul : 8ul;

    protected bool _hasFinishedInitialRead;
    private bool _inReadableRead;
    public ConcurrentDictionary<Type, int> BytesReadPerClass = new();


    public ClassReadingBinaryReader(MemoryStream input) : base(input)
    {
        _memoryStream = input;
    }

    public ClassReadingBinaryReader(Stream input) : base(input)
    {
        _memoryStream = null;
    }

    public long Position
    {
        get => BaseStream.Position;
        set => BaseStream.Position = value;
    }

    public long Length => BaseStream.Length;

    internal virtual object? ReadPrimitive(Type type, bool overrideArchCheck = false)
    {
        if (type == typeof(bool))
            return ReadBoolean();

        if (type == typeof(char))
            return ReadChar();

        if (type == typeof(int))
            return ReadInt32();

        if (type == typeof(uint))
            return ReadUInt32();

        if (type == typeof(short))
            return ReadInt16();

        if (type == typeof(ushort))
            return ReadUInt16();

        if (type == typeof(sbyte))
            return ReadSByte();

        if (type == typeof(byte))
            return ReadByte();

        if (type == typeof(long))
            return is32Bit && !overrideArchCheck ? ReadInt32() : ReadInt64();

        if (type == typeof(ulong))
            return is32Bit && !overrideArchCheck ? ReadUInt32() : ReadUInt64();

        if (type == typeof(float))
            return ReadSingle();

        if (type == typeof(double))
            return ReadDouble();

        return null;
    }

    public uint ReadUnityCompressedUIntAtRawAddr(long offset, out int bytesRead)
    {
        GetLockOrThrow();

        try
        {
            return ReadUnityCompressedUIntAtRawAddrNoLock(offset, out bytesRead);
        }
        finally
        {
            ReleaseLock();
        }
    }

    protected internal uint ReadUnityCompressedUIntAtRawAddrNoLock(long offset, out int bytesRead)
    {
        if (offset >= 0)
            Position = offset;

        //Ref Unity.IL2CPP.dll, Unity.IL2CPP.Metadata.MetadataUtils::WriteCompressedUInt32
        //Read first byte
        var b = ReadByte();
        bytesRead = 1;
        if (b < 128)
            return b;
        if (b == 240)
        {
            //Full Uint
            bytesRead = 5;
            return ReadUInt32();
        }

        //Special constant values
        if (b == byte.MaxValue)
            return uint.MaxValue;
        if (b == 254)
            return uint.MaxValue - 1;

        if ((b & 192) == 192)
        {
            //3 more to read
            bytesRead = 4;
            return (b & ~192U) << 24 | (uint)(ReadByte() << 16) | (uint)(ReadByte() << 8) | ReadByte();
        }

        if ((b & 128) == 128)
        {
            //1 more to read
            bytesRead = 2;
            return (b & ~128U) << 8 | ReadByte();
        }


        throw new Exception($"How did we even get here? Invalid compressed int first byte {b}");
    }

    public int ReadUnityCompressedIntAtRawAddr(long position, out int bytesRead)
        => ReadUnityCompressedIntAtRawAddr(position, true, out bytesRead);

    protected internal int ReadUnityCompressedIntAtRawAddr(long position, bool doLock, out int bytesRead)
    {
        //Ref libil2cpp, il2cpp\utils\ReadCompressedInt32
        uint unsigned;
        if (doLock)
            unsigned = ReadUnityCompressedUIntAtRawAddr(position, out bytesRead);
        else
            unsigned = ReadUnityCompressedUIntAtRawAddrNoLock(position, out bytesRead);

        if (unsigned == uint.MaxValue)
            return int.MinValue;

        var isNegative = (unsigned & 1) == 1;
        unsigned >>= 1;
        if (isNegative)
            return -(int)(unsigned + 1);

        return (int)unsigned;
    }

    private T InternalReadClass<T>(bool overrideArchCheck = false) where T : new()
    {
        return (T)InternalReadClass(typeof(T), overrideArchCheck);
    }

    private T InternalReadReadableClass<T>() where T : ReadableClass, new()
    {
        var t = new T();

        if (!_inReadableRead)
        {
            _inReadableRead = true;
            t.Read(this);
            _inReadableRead = false;
        }
        else
        {
            t.Read(this);
        }

        return t;
    }

    private object InternalReadClass(Type type, bool overrideArchCheck = false)
    {
        if (type.IsPrimitive)
        {
            return ReadAndConvertPrimitive(overrideArchCheck, type);
        }

        if (type.IsEnum)
        {
            var value = ReadPrimitive(type.GetEnumUnderlyingType());

            return value!;
        }

        throw new("Support for reading classes has been removed. Please inherit from ReadableClass and call ReadReadable on your local binary reader.");
    }

    private object ReadAndConvertPrimitive(bool overrideArchCheck, Type type)
    {
        var value = ReadPrimitive(type, overrideArchCheck);

        //32-bit fixes...
        if (value is uint && type == typeof(ulong))
            value = Convert.ToUInt64(value);
        if (value is int && type == typeof(long))
            value = Convert.ToInt64(value);

        return value!;
    }

    public T[] ReadClassArrayAtRawAddr<T>(long offset, long count) where T : new()
    {
        var t = new T[count];

        GetLockOrThrow();

        try
        {
            if (offset != -1) Position = offset;

            for (var i = 0; i < count; i++)
            {
                t[i] = InternalReadClass<T>();
            }

            return t;
        }
        finally
        {
            ReleaseLock();
        }
    }

    public string ReadStringToNull(ulong offset) => ReadStringToNull((long)offset);

    public virtual string ReadStringToNull(long offset)
    {
        GetLockOrThrow();

        try
        {
            return ReadStringToNullNoLock(offset);
        }
        finally
        {
            ReleaseLock();
        }
    }

    internal string ReadStringToNullNoLock(long offset)
    {
        var builder = new List<byte>();

        if (offset != -1)
            Position = offset;

        try
        {
            byte b;
            while ((b = ReadByte()) != 0)
                builder.Add(b);

            return Encoding.UTF8.GetString(builder.ToArray());
        }
        finally
        {
            var bytesRead = (int)(Position - offset);
            TrackRead<string>(bytesRead);
        }
    }

    public string ReadStringToNullAtCurrentPos()
        => ReadStringToNullNoLock(-1);

    public byte[] ReadByteArrayAtRawAddress(long offset, int count)
    {
        GetLockOrThrow();

        try
        {
            return ReadByteArrayAtRawAddressNoLock(offset, count);
        }
        finally
        {
            ReleaseLock();
        }
    }

    protected internal byte[] ReadByteArrayAtRawAddressNoLock(long offset, int count)
    {
        if (offset != -1)
            Position = offset;

        if (count == 0)
            return [];

        try
        {
            var ret = new byte[count];
            Read(ret, 0, count);

            return ret;
        }
        finally
        {
            TrackRead<byte>(count, false);
        }
    }

    protected internal void GetLockOrThrow()
    {
        var obtained = false;
        PositionShiftLock.Enter(ref obtained);

        if (!obtained)
            throw new Exception("Failed to obtain lock");
    }

    protected internal void ReleaseLock()
    {
        PositionShiftLock.Exit();
    }

    public ulong ReadNUintAtRawAddress(long offset)
    {
        if (offset > Length)
            throw new EndOfStreamException($"ReadNUintAtRawAddress: Offset 0x{offset:X} is beyond the end of the stream (length 0x{Length:X})");

        GetLockOrThrow();

        try
        {
            Position = offset;
            return ReadNUint();
        }
        finally
        {
            ReleaseLock();

            TrackRead<ulong>((int)PointerSize, false);
        }
    }

    public ulong[] ReadNUintArrayAtRawAddress(long offset, int count)
    {
        if (offset > Length)
            throw new EndOfStreamException($"ReadNUintArrayAtRawAddress: Offset 0x{offset:X} is beyond the end of the stream (length 0x{Length:X})");

        var inBounds = offset + count * (int)PointerSize <= Length;
        if (!inBounds)
            throw new EndOfStreamException($"ReadNUintArrayAtRawAddress: Attempted to read {count} pointers (pointer length {PointerSize}) at offset 0x{offset:X}, but this goes beyond the end of the stream (length 0x{Length:X})");

        GetLockOrThrow();

        try
        {
            Position = offset;

            var ret = new ulong[count];

            for (var i = 0; i < count; i++)
            {
                ret[i] = ReadNUint();
            }

            return ret;
        }
        finally
        {
            ReleaseLock();

            var bytesRead = count * (int)PointerSize;
            TrackRead<ulong>(bytesRead, false);
        }
    }


    /// <summary>
    /// Read a native-sized integer (i.e. 32 or 64 bit, depending on platform) at the current position
    /// </summary>
    public virtual long ReadNInt() => is32Bit ? ReadInt32() : ReadInt64();

    /// <summary>
    /// Read a native-sized unsigned integer (i.e. 32 or 64 bit, depending on platform) at the current position
    /// </summary>
    public virtual ulong ReadNUint() => is32Bit ? ReadUInt32() : ReadUInt64();

    protected void WriteWord(int position, ulong word) => WriteWord(position, (long)word);

    /// <summary>
    /// Used for ELF Relocations.
    /// </summary>
    protected void WriteWord(int position, long word)
    {
        if (_memoryStream == null)
            throw new("WriteWord is not supported in non-memory-backed readers");

        GetLockOrThrow();

        try
        {
            byte[] rawBytes;
            if (is32Bit)
            {
                var value = (int)word;
                rawBytes = BitConverter.GetBytes(value);
            }
            else
            {
                rawBytes = BitConverter.GetBytes(word);
            }

            if (ShouldReverseArrays)
                rawBytes = rawBytes.Reverse();

            if (position > _memoryStream.Length)
                throw new Exception($"WriteWord: Position {position} beyond length {_memoryStream.Length}");

            var count = is32Bit ? 4 : 8;
            if (position + count > _memoryStream.Length)
                throw new Exception($"WriteWord: Writing {count} bytes at {position} would go beyond length {_memoryStream.Length}");

            if (rawBytes.Length != count)
                throw new Exception($"WriteWord: Expected {count} bytes from BitConverter, got {position}");

            try
            {
                _memoryStream.Seek(position, SeekOrigin.Begin);
                _memoryStream.Write(rawBytes, 0, count);
            }
            catch
            {
                Logging.LibLogger.ErrorNewline("WriteWord: Unexpected exception!");
                throw;
            }
        }
        finally
        {
            ReleaseLock();
        }
    }

    public T ReadReadableHereNoLock<T>() where T : ReadableClass, new() => InternalReadReadableClass<T>();

    public T ReadReadable<T>(long offset = -1) where T : ReadableClass, new()
    {
        GetLockOrThrow();

        if (offset >= 0)
            Position = offset;

        var initialPos = Position;

        try
        {
            return InternalReadReadableClass<T>();
        }
        finally
        {
            var bytesRead = (int)(Position - initialPos);
            TrackRead<T>(bytesRead, trackIfFinishedReading: true);

            ReleaseLock();
        }
    }

    public T[] ReadReadableArrayAtRawAddr<T>(long offset, long count) where T : ReadableClass, new()
    {
        var t = new T[count];

        GetLockOrThrow();

        if (offset != -1)
            Position = offset;

        try
        {
            //This handles the actual reading into the array, and tracking read counts, for us.
            FillReadableArrayHereNoLock(t);
        }
        finally
        {
            ReleaseLock();
        }

        return t;
    }

    public void FillReadableArrayHereNoLock<T>(T[] array, int startOffset = 0) where T : ReadableClass, new()
    {
        var initialPos = Position;

        try
        {
            var i = startOffset;
            for (; i < array.Length; i++)
            {
                array[i] = InternalReadReadableClass<T>();
            }
        }
        finally
        {
            var bytesRead = (int)(Position - initialPos);
            TrackRead<T>(bytesRead, trackIfFinishedReading: true);
        }
    }

    public void TrackRead<T>(int bytesRead, bool trackIfInReadableRead = true, bool trackIfFinishedReading = false)
    {
        if (!EnableReadableSizeInformation)
            return;

        if (!trackIfInReadableRead && _inReadableRead)
            return;

        if (_hasFinishedInitialRead && !trackIfFinishedReading)
            return;

        BytesReadPerClass[typeof(T)] = BytesReadPerClass.GetOrDefault(typeof(T)) + bytesRead;
    }
}
