using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace LibCpp2IL
{
    public class ClassReadingBinaryReader : EndianAwareBinaryReader
    {
        private SpinLock PositionShiftLock;

        private MethodInfo readClass;
        public bool is32Bit;
        private MemoryStream _memoryStream;


        public ClassReadingBinaryReader(MemoryStream input) : base(input)
        {
            readClass = typeof(ClassReadingBinaryReader).GetMethod(nameof(InternalReadClass), BindingFlags.Instance | BindingFlags.NonPublic)!;
            _memoryStream = input;
        }

        public long Position
        {
            get => BaseStream.Position;
            protected set => BaseStream.Position = value;
        }

        internal object? ReadPrimitive(Type type, bool overrideArchCheck = false)
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
        
        private Dictionary<FieldInfo, bool> _cachedNoSerialize = new();

        public T ReadClassAtRawAddr<T>(long offset, bool overrideArchCheck = false) where T : new()
        {
            var obtained = false;
            PositionShiftLock.Enter(ref obtained);

            if (!obtained)
                throw new Exception("Failed to obtain lock");

            try
            {
                if (offset >= 0)
                    Position = offset;


                return InternalReadClass<T>(overrideArchCheck);
            }
            finally
            {
                PositionShiftLock.Exit();
            }
        }

        public uint ReadUnityCompressedUIntAtRawAddr(long offset, out int bytesRead)
        {
            var obtained = false;
            PositionShiftLock.Enter(ref obtained);

            if (!obtained)
                throw new Exception("Failed to obtain lock");

            try
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
            finally
            {
                PositionShiftLock.Exit();
            }
        }

        public int ReadUnityCompressedIntAtRawAddr(long position, out int bytesRead)
        {
            //Ref libil2cpp, il2cpp\utils\ReadCompressedInt32
            var unsigned = ReadUnityCompressedUIntAtRawAddr(position, out bytesRead);

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
            var t = new T();

            var type = typeof(T);
            if (type.IsPrimitive)
            {
                var value = ReadPrimitive(type, overrideArchCheck);

                //32-bit fixes...
                if (value is uint && typeof(T) == typeof(ulong))
                    value = Convert.ToUInt64(value);
                if (value is int && typeof(T) == typeof(long))
                    value = Convert.ToInt64(value);

                return (T) value!;
            }

            if (type.IsEnum)
            {
                var value = ReadPrimitive(type.GetEnumUnderlyingType());

                return (T) value!;
            }

            foreach (var field in t.GetType().GetFields())
            {
                if (!_cachedNoSerialize.ContainsKey(field))
                    _cachedNoSerialize[field] = Attribute.GetCustomAttribute(field, typeof(NonSerializedAttribute)) != null;

                if (_cachedNoSerialize[field]) continue;

                if(!LibCpp2ILUtils.ShouldReadFieldOnThisVersion(field))
                    continue;

                if (field.FieldType.IsPrimitive)
                {
                    field.SetValue(t, ReadPrimitive(field.FieldType));
                }
                else
                {
                    var gm = readClass.MakeGenericMethod(field.FieldType);
                    var o = gm.Invoke(this, new object[] {overrideArchCheck});
                    field.SetValue(t, o);
                }
            }

            return t;
        }

        public T[] ReadClassArrayAtRawAddr<T>(ulong offset, ulong count) where T : new() => ReadClassArrayAtRawAddr<T>((long) offset, (long) count);

        public T[] ReadClassArrayAtRawAddr<T>(long offset, long count) where T : new()
        {
            var t = new T[count];

            var obtained = false;
            PositionShiftLock.Enter(ref obtained);

            if (!obtained)
                throw new Exception("Failed to obtain lock");

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
                PositionShiftLock.Exit();
            }
        }

        public string ReadStringToNull(ulong offset) => ReadStringToNull((long) offset);

        public string ReadStringToNull(long offset)
        {
            var builder = new List<byte>();

            var obtained = false;
            PositionShiftLock.Enter(ref obtained);

            if (!obtained)
                throw new Exception("Failed to obtain lock");

            try
            {
                Position = offset;
                byte b;
                while ((b = (byte) _memoryStream.ReadByte()) != 0)
                    builder.Add(b);


                return Encoding.Default.GetString(builder.ToArray());
            }
            finally
            {
                PositionShiftLock.Exit();
            }
        }

        public byte[] ReadByteArrayAtRawAddress(long offset, int count)
        {
            var obtained = false;
            PositionShiftLock.Enter(ref obtained);

            if (!obtained)
                throw new Exception("Failed to obtain lock");

            try
            {
                Position = offset;
                var ret = new byte[count];
                Read(ret, 0, count);
                
                return ret;
            }
            finally
            {
                PositionShiftLock.Exit();
            }
        }

        protected void WriteWord(int position, ulong word) => WriteWord(position, (long) word);

        /// <summary>
        /// Used for ELF Relocations.
        /// </summary>
        protected void WriteWord(int position, long word)
        {
            var obtained = false;
            PositionShiftLock.Enter(ref obtained);

            if (!obtained)
                throw new Exception("Failed to obtain lock");

            try
            {
                byte[] rawBytes;
                if (is32Bit)
                {
                    var value = (int) word;
                    rawBytes = BitConverter.GetBytes(value);
                }
                else
                {
                    rawBytes = BitConverter.GetBytes(word);
                }

                if (shouldReverseArrays)
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
                PositionShiftLock.Exit();
            }
        }
    }
}