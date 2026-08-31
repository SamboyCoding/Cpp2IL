using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LibCpp2IL;
#if DEBUG
using System.Diagnostics;
#endif

namespace Cpp2IL.Core.Utils;

public static class MiscUtils
{
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

    public static int GetSlotNum(int offset, float metadataVersion, bool is32Bit)
    {
        var offsetInVtable = offset - Il2CppClassUsefulOffsets.GetVtableOffset(metadataVersion, is32Bit); //0x128 being the address of the vtable in an Il2CppClass

        if (offsetInVtable % 0x10 != 0 && offsetInVtable % 0x8 == 0)
            offsetInVtable -= 0x8; //Handle read of the second pointer in the struct.

        if (offsetInVtable > 0)
        {
            var slotNum = (decimal)offsetInVtable / 0x10;

            return (int)slotNum;
        }

        return -1;
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

#if DEBUG
        if (Debugger.IsAttached)
        {
            ExecuteSerial(enumerable, what);
            return;
        }
#endif

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
        "crashpad_handler.exe",
        "EOSBootstrapper.exe",
        "start_protected_game.exe"
    ];

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

    public static string ToCollapsedString(this Exception ex)
    {
        if (ex == null) return string.Empty;

        var s = ex.ToString();
        var lines = s.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

        var result = new List<string>();
        var repeatCount = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0 && lines[i] == lines[i - 1])
            {
                repeatCount++;
            }
            else
            {
                if (repeatCount > 0)
                {
                    result.Add($"   ... repeated {repeatCount} times ...");
                    repeatCount = 0;
                }
                result.Add(lines[i]);
            }
        }

        if (repeatCount > 0)
            result.Add($"   ... repeated {repeatCount} times ...");

        return string.Join(Environment.NewLine, result);
    }
}
