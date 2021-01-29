using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace LibCpp2IL
{
    public class ClassReadingBinaryReader : BinaryReader
    {
        private readonly object PositionShiftLock = new object();
        
        private MethodInfo readClass;
        public bool is32Bit;
        private MemoryStream _memoryStream;


        public ClassReadingBinaryReader(MemoryStream input) : base(input)
        {
            readClass = GetType().GetMethod("ReadClass")!;
            _memoryStream = input;
        }

        public long Position
        {
            get => BaseStream.Position;
            set => BaseStream.Position = value;
        }

        private object? ReadPrimitive(Type type)
        {
            if (type == typeof(int))
                return ReadInt32();

            if (type == typeof(uint))
                return ReadUInt32();

            if (type == typeof(short))
                return ReadInt16();
            
            if(type == typeof(ushort))
                return ReadUInt16();
            
            if(type == typeof(byte))
                return ReadByte();

            if (type == typeof(long))
                return is32Bit ? ReadInt32() : ReadInt64();
            
            if (type == typeof(ulong))
                return is32Bit ? ReadUInt32() : ReadUInt64();

            return null;
        }

        private Dictionary<FieldInfo, VersionAttribute?> _cachedVersionAttributes = new Dictionary<FieldInfo, VersionAttribute?>();
        private Dictionary<FieldInfo, bool> _cachedNoSerialize = new Dictionary<FieldInfo, bool>();

        public T ReadClass<T>(long offset) where T : new()
        {
            var t = new T();
            lock (PositionShiftLock)
            {
                if (offset >= 0) Position = offset;

                var type = typeof(T);
                if (type.IsPrimitive)
                {
                    var value = ReadPrimitive(type);

                    //32-bit fixes...
                    if (value is uint && typeof(T) == typeof(ulong))
                        value = Convert.ToUInt64(value);
                    if (value is int && typeof(T) == typeof(long))
                        value = Convert.ToInt64(value);

                    return (T) value!;
                }
                
                foreach (var i in t.GetType().GetFields())
                {
                    VersionAttribute? attr;
                    
                    if(!_cachedNoSerialize.ContainsKey(i))
                        _cachedNoSerialize[i] = Attribute.GetCustomAttribute(i, typeof(NonSerializedAttribute)) != null;

                    if (_cachedNoSerialize[i]) continue;
                    
                    if (!_cachedVersionAttributes.ContainsKey(i))
                    {
                        attr = (VersionAttribute?) Attribute.GetCustomAttribute(i, typeof(VersionAttribute));
                        _cachedVersionAttributes[i] = attr;
                    }
                    else
                        attr = _cachedVersionAttributes[i];

                    if (attr != null)
                    {
                        if (LibCpp2IlMain.MetadataVersion < attr.Min || LibCpp2IlMain.MetadataVersion > attr.Max)
                            continue;
                    }

                    if (i.FieldType.IsPrimitive)
                    {
                        i.SetValue(t, ReadPrimitive(i.FieldType));
                    }
                    else
                    {
                        var gm = readClass.MakeGenericMethod(i.FieldType);
                        var o = gm.Invoke(this, new object[] {-1});
                        i.SetValue(t, o);
                        break;
                    }
                }
            }

            return t;
        }

        public T[] ReadClassArray<T>(long offset, long count) where T : new()
        {
            var t = new T[count];
            lock (PositionShiftLock)
            {

                if (offset != -1) Position = offset;

                for (var i = 0; i < count; i++)
                {
                    t[i] = ReadClass<T>(-1);
                }
            }

            return t;
        }

        public string ReadStringToNull(long offset)
        {
            var builder = new List<byte>();
            lock (PositionShiftLock)
            {
                Position = offset;
                byte b;
                while ((b = (byte) _memoryStream.ReadByte()) != 0)
                    builder.Add(b);
            }

            return Encoding.Default.GetString(builder.ToArray());
        }
    }
}