using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL;

namespace Cpp2IL.Core.Utils;

public static class MiscUtils
{
    private static List<ulong>? _allKnownFunctionStarts;

    private static Dictionary<string, ulong> _primitiveSizes = new();

    public static readonly List<char> InvalidPathChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    public static readonly HashSet<string> InvalidPathElements =
    [
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    ];

    internal static void Reset()
    {
        _allKnownFunctionStarts = null;
    }

    internal static void Init()
    {
        _primitiveSizes = new(14)
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
        if ((ulong)theDll.RawLength <= rawAddr)
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

    internal static byte[] RawBytes(IConvertible original) =>
        original switch
        {
            bool b => BitConverter.GetBytes(b),
            char c => BitConverter.GetBytes(c),
            byte b => [b],
            sbyte sb => [unchecked((byte)sb)],
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

    //TODO: Refactor this out to a property of ApplicationAnalysisContext
    internal static void InitFunctionStarts(ApplicationAnalysisContext appContext)
    {
        _allKnownFunctionStarts = appContext.Metadata.methodDefs.Select(m => m.MethodPointer)
            .Concat(appContext.Binary.ConcreteGenericImplementationsByAddress.Keys)
            .Concat(SharedState.AttributeGeneratorStarts)
            .ToList();

        //Sort in ascending order
        _allKnownFunctionStarts.Sort();
    }
    //TODO: End

    public static ulong GetAddressOfNextFunctionStart(ulong current)
    {
        if (_allKnownFunctionStarts == null)
            throw new("Function starts not initialized!");

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

        if (!LibCpp2IlMain.Binary!.TryMapVirtualAddressToRaw(ret, out _))
            return 0;

        return ret;
    }

    public static void ExecuteSerial<T>(IEnumerable<T> enumerable, Action<T> what)
    {
        foreach (var item in enumerable)
        {
            what(item);
        }
    }

    public static void ExecuteParallel<T>(IEnumerable<T> enumerable, Action<T> what)
    {
        bool F2(T t)
        {
            what(t);
            return true;
        }

        enumerable
            .AsParallel()
            .Select((Func<T, bool>)F2)
            .ToList();
    }

    public static readonly string[] BlacklistedExecutableFilenames =
    [
        "UnityCrashHandler.exe",
        "UnityCrashHandler32.exe",
        "UnityCrashHandler64.exe",
        "install.exe",
        "launch.exe",
        "MelonLoader.Installer.exe",
        "crashpad_handler.exe"
    ];

    public static string AnalyzeStackTracePointers(ulong[] pointers)
    {
        // var pointers = new ulong[] {0x52e6ba0, 0x52ad3a0, 0x11b09714, 0x40a990c, 0xd172c68, 0xa2c0514, 0x35ea45c, 0x1fc43208};

        var methodsSortedByPointer = Cpp2IlApi.CurrentAppContext!.Metadata.methodDefs.ToList();
        methodsSortedByPointer.SortByExtractedKey(m => m.MethodPointer);

        var genericMethodsSortedByPointer = Cpp2IlApi.CurrentAppContext.Binary.ConcreteGenericImplementationsByAddress.ToList();
        genericMethodsSortedByPointer.SortByExtractedKey(m => m.Key);

        var stack = pointers.Select(p =>
        {
            var method = methodsSortedByPointer.LastOrDefault(m => m.MethodPointer <= p);
            var genericMethod = genericMethodsSortedByPointer.LastOrDefault(m => m.Key <= p);

            if (method == null || genericMethod.Key == 0)
                return "<unknown method>";

            var distanceNormal = p - method.MethodPointer;
            var distanceGeneric = p - genericMethod.Key;

            if (Math.Min(distanceGeneric, distanceNormal) > 0x50000)
                return "<unknown method>";

            if (distanceGeneric < distanceNormal)
            {
                var actualGen = genericMethod.Value.First();
                return actualGen.DeclaringType.DeclaringAssembly!.Name + " ## " + actualGen + "(" + string.Join(", ", actualGen.BaseMethod.Parameters!.ToList()) + ")";
            }

            return method.DeclaringType!.DeclaringAssembly!.Name + " ## " + method.DeclaringType.FullName + "::" + method.Name + "(" + string.Join(", ", method.Parameters!.ToList()) + ")";
        });

        return string.Join("\n", stack);
    }

    /// <summary>
    /// Returns the input string with any invalid path characters removed.
    /// </summary>
    /// <param name="input">The string to clean up</param>
    /// <returns>The input string with any characters that are invalid in the NTFS file system replaced with underscores, and additionally escaped if they collide with legacy dos device names.</returns>
    public static string CleanPathElement(string input)
    {
        InvalidPathChars.ForEach(c => input = input.Replace(c, '_'));

        return InvalidPathElements.Contains(input) ? $"__invalidwin32name_{input}__" : input;
    }
}
