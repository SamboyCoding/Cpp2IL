using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Extensions;
using LibCpp2IL;

namespace Cpp2IL.Core.Utils
{
    public static class MiscUtils
    {
        private static List<ulong>? _allKnownFunctionStarts;

        private static Dictionary<string, ulong> PrimitiveSizes;

        internal static void Reset()
        {
            TypeDefinitionsAsmResolver.Reset();
            _allKnownFunctionStarts = null;
        }

        internal static void Init()
        {
            PrimitiveSizes = new(14)
            {
                { "Byte", 1 },
                { "SByte", 1 },
                { "Boolean", 1 },
                { "Int16", 2 },
                { "UInt16", 2 },
                { "Char", 2 },
                { "Int32", 4 },
                { "UInt32", 4 },
                { "Single", 4 },
                { "Int64", 8 },
                { "UInt64", 8 },
                { "Double", 8 },
                { "IntPtr", LibCpp2IlMain.Binary!.is32Bit ? 4UL : 8UL },
                { "UIntPtr", LibCpp2IlMain.Binary.is32Bit ? 4UL : 8UL },
            };
        }


        internal static string[] GetGenericParams(string input)
        {
            if (!input.Contains('<'))
                return input.Split(',');

            var depth = 0;
            var ret = new List<string>();
            var sb = new StringBuilder();

            foreach (var c in input)
            {
                if (c == '<')
                    depth++;
                if (c == '>')
                    depth--;
                if (depth == 0 && c == ',')
                {
                    ret.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }
            ret.Add(sb.ToString());

            return ret.ToArray();
        }

        public static string? TryGetLiteralAt(Il2CppBinary theDll, ulong rawAddr)
        {
            if (theDll.RawLength <= (long)rawAddr)
                return null;

            var c = Convert.ToChar(theDll.GetByteAtRawAddress(rawAddr));
            if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c))
            {
                var isUnicode = theDll.GetByteAtRawAddress(rawAddr + 1) == 0 && theDll.GetByteAtRawAddress(rawAddr + 3) == 0;
                var literal = new StringBuilder();
                while ((theDll.GetByteAtRawAddress(rawAddr) != 0 || isUnicode && theDll.GetByteAtRawAddress(rawAddr + 1) != 0) && literal.Length < 5000)
                {
                    literal.Append(Convert.ToChar(theDll.GetByteAtRawAddress(rawAddr)));
                    rawAddr++;
                    if (isUnicode) rawAddr++;
                }

                var wasNullTerminated = theDll.GetByteAtRawAddress(rawAddr) == 0;

                if (literal.Length >= 4 || (wasNullTerminated))
                {
                    return literal.ToString();
                }
            }
            else if (c == '\0')
                return string.Empty;

            return null;
        }

        public static int GetSlotNum(int offset)
        {
            var offsetInVtable = offset - Il2CppClassUsefulOffsets.VTABLE_OFFSET; //0x128 being the address of the vtable in an Il2CppClass

            if (offsetInVtable % 0x10 != 0 && offsetInVtable % 0x8 == 0)
                offsetInVtable -= 0x8; //Handle read of the second pointer in the struct.

            if (offsetInVtable > 0)
            {
                var slotNum = (decimal)offsetInVtable / 0x10;

                return (int)slotNum;
            }

            return -1;
        }

        public static int GetPointerSizeBytes()
        {
            return LibCpp2IlMain.Binary!.is32Bit ? 4 : 8;
        }

        public static IConvertible ReinterpretBytes(IConvertible original, Type desired)
        {
            if (desired is null)
                throw new ArgumentNullException(nameof(desired), "Destination type is null");
            
            var rawBytes = RawBytes(original);

            if (!typeof(IConvertible).IsAssignableFrom(desired))
                throw new Exception($"ReinterpretBytes: Desired type, {desired}, does not implement IConvertible");
            
            //Pad out with leading zeros if we have to
            var requiredLength = LibCpp2ILUtils.VersionAwareSizeOf(desired);

            if (requiredLength > rawBytes.Length)
            {
                rawBytes = ((byte) 0).Repeat(requiredLength - rawBytes.Length).Concat(rawBytes).ToArray();
            }

            if (desired == typeof(bool))
                return BitConverter.ToBoolean(rawBytes, 0);
            if (desired == typeof(byte))
                return rawBytes[0];
            if (desired == typeof(char))
                return BitConverter.ToChar(rawBytes, 0);
            if (desired == typeof(sbyte))
                return unchecked((sbyte)rawBytes[0]);
            if (desired == typeof(ushort))
                return BitConverter.ToUInt16(rawBytes, 0);
            if (desired == typeof(short))
                return BitConverter.ToInt16(rawBytes,0);
            if (desired == typeof(uint))
                return BitConverter.ToUInt32(rawBytes, 0);
            if (desired == typeof(int))
                return BitConverter.ToInt32(rawBytes, 0);
            if (desired == typeof(ulong))
                return BitConverter.ToUInt64(rawBytes, 0);
            if (desired == typeof(long))
                return BitConverter.ToInt64(rawBytes, 0);
            if (desired == typeof(float))
                return BitConverter.ToSingle(rawBytes, 0);
            if(desired == typeof(double))
                return BitConverter.ToDouble(rawBytes, 0);

            throw new($"ReinterpretBytes: Cannot convert byte array back to a type of {desired}");
        }

        internal static byte[] RawBytes(IConvertible original) =>
            original switch
            {
                bool b => BitConverter.GetBytes(b),
                char c => BitConverter.GetBytes(c),
                byte b => BitConverter.GetBytes(b),
                sbyte sb => BitConverter.GetBytes(sb),
                ushort us => BitConverter.GetBytes(us),
                short s => BitConverter.GetBytes(s),
                uint ui => BitConverter.GetBytes(ui),
                int i => BitConverter.GetBytes(i),
                ulong ul => BitConverter.GetBytes(ul),
                long l => BitConverter.GetBytes(l),
                float f => BitConverter.GetBytes(f),
                double d => BitConverter.GetBytes(d),
                _ => throw new($"ReinterpretBytes: Cannot get byte array from {original} (type {original.GetType()}")
            };

        private static void InitFunctionStarts()
        {
            _allKnownFunctionStarts = LibCpp2IlMain.TheMetadata!.methodDefs.Select(m => m.MethodPointer)
                .Concat(LibCpp2IlMain.Binary!.ConcreteGenericImplementationsByAddress.Keys)
                .Concat(SharedState.AttributeGeneratorStarts)
                .ToList();

            //Sort in ascending order
            _allKnownFunctionStarts.Sort();
        }

        public static ulong GetAddressOfNextFunctionStart(ulong current)
        {
            if(_allKnownFunctionStarts == null)
                InitFunctionStarts();

            //Binary-search-like approach
            var lower = 0;
            var upper = _allKnownFunctionStarts!.Count - 1;

            var ret = ulong.MaxValue;
            while (upper - lower >= 1)
            {
                var pos = (upper - lower) / 2 + lower;

                if (upper - lower == 1)
                    pos = lower;

                var ptr = _allKnownFunctionStarts[pos];
                if (ptr > current)
                {
                    //This matches what we want to look for
                    if (ptr < ret)
                        //This is a better "next method" pointer
                        ret = ptr;

                    //Either way, we're above our current address now, so search lower in the list
                    upper = pos;
                }
                else
                {
                    //Not what we want, so move up in the list
                    lower = pos + 1;
                }
            }
            ret = _allKnownFunctionStarts[lower];
            if (ret < current)
                ret = _allKnownFunctionStarts[upper];

            if (ret <= current && upper == _allKnownFunctionStarts.Count - 1)
                return 0;

            return ret;
        }

        public static bool BitsAreEqual(this BitArray first, BitArray second)
        {
            if (first.Count != second.Count)
                return false;
            
            bool areDifferent = false;
            for (int i = 0; i < first.Count && !areDifferent; i++)
                areDifferent =  first.Get(i) != second.Get(i);

            return !areDifferent;
        }

        public static void ExecuteParallel<T>(IEnumerable<T> enumerable, Action<T> what)
        {
            var f2 = (T t) =>
            {
                what(t);
                return true;
            };
            
            enumerable
                // .AsParallel()
                .Select(f2)
                .ToList();
        }

        public static readonly string[] BlacklistedExecutableFilenames =
        {
            "UnityCrashHandler.exe",
            "UnityCrashHandler32.exe",
            "UnityCrashHandler64.exe",
            "install.exe",
            "launch.exe",
            "MelonLoader.Installer.exe"
        };
    }
}
