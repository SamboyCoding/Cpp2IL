using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace LibCpp2IL.Metadata;

public class Il2CppAssemblyNameDefinition : ReadableClass
{
    public int nameIndex;
    public int cultureIndex;

    [Version(Max = 24.1f)] [Version(Min = 24.2f, Max = 24.3f)] //Not present in 24.15
    public int hashValueIndex;

    public int publicKeyIndex;
    public uint hash_alg;
    public int hash_len;
    public uint flags;
    public int major;
    public int minor;
    public int build;
    public int revision;
    public ulong publicKeyToken;

    public string Name => LibCpp2IlMain.TheMetadata!.GetStringFromIndex(nameIndex);
    public string Culture => LibCpp2IlMain.TheMetadata!.GetStringFromIndex(cultureIndex);

    public byte[]? PublicKey
    {
        get
        {
            // The difference between these two options can be seen in the header files.
            // const uint8_t* public_key;
            // const char* public_key;

            if (IsAtLeast(24.4f) || Is(24.15f))
            {
                // 2018.4.34 until 2019 (24.15)
                // 2019.4.15 until 2020 (24.4)
                // 2020.1.11 until 2020.2.0 (24.4)
                // 2020.2.0b7 and later (27+)
                var result = LibCpp2IlMain.TheMetadata!.GetByteArrayFromIndex(publicKeyIndex);
                return result.Length == 0 ? null : result;
            }
            else
            {
                var str = LibCpp2IlMain.TheMetadata!.GetStringFromIndex(publicKeyIndex);

                if (str is "NULL")
                    return null; // No public key

                // mscorlib example:
                // "\x0\x0\x0\x0\x0\x0\x0\x0\x4\x0\x0\x0\x0\x0\x0\x0"
                // The string above is verbatim, so the backslashes are not escape characters and the quotes are part of the string.
                if (str.Length > 2 && str[0] is '"' && str[^1] is '"' && TryParseByteArray(str.AsSpan()[1..^1], out var result))
                    return result;

                return null; // Parsing failed
            }

            static bool IsHexDigit(char c) => c is (>= '0' and <= '9') or (>= 'A' and <= 'F') or (>= 'a' and <= 'f');

            static bool TryParseByte(ReadOnlySpan<char> characters, out byte result)
            {
#if NET6_0_OR_GREATER
                return byte.TryParse(characters, System.Globalization.NumberStyles.HexNumber, null, out result);
#else
                return byte.TryParse(characters.ToString(), System.Globalization.NumberStyles.HexNumber, null, out result);
#endif
            }

            static bool TryParseByteArray(ReadOnlySpan<char> characters, [NotNullWhen(true)] out byte[]? result)
            {
                var bytes = new List<byte>();

                var i = 0;
                while (i < characters.Length)
                {
                    // Each byte is represented by 3 or 4 characters.
                    // The first one is backslash.
                    // The second one is 'x'.
                    // The third and fourth (if present) ones are the hexadecimal representation of the byte.
                    if (i > characters.Length - 3 || characters[i] is not '\\' || characters[i + 1] is not 'x')
                    {
                        result = null;
                        return false;
                    }

                    i += 2;

                    if (!IsHexDigit(characters[i]))
                    {
                        result = null;
                        return false;
                    }

                    ReadOnlySpan<char> hexCharacters;
                    if (i + 1 < characters.Length && characters[i + 1] is not '\\')
                    {
                        if (!IsHexDigit(characters[i + 1]))
                        {
                            result = null;
                            return false;
                        }
                        hexCharacters = characters.Slice(i, 2);
                        i += 2;
                    }
                    else
                    {
                        hexCharacters = characters.Slice(i, 1);
                        i += 1;
                    }

                    if (!TryParseByte(hexCharacters, out var b))
                    {
                        result = null;
                        return false;
                    }

                    bytes.Add(b);
                }

                result = bytes.ToArray();
                return true;
            }
        }
    }

    /// <summary>
    /// This returns the public key token as a byte array, or null if the token is 0.
    /// </summary>
    /// <remarks>
    /// Returning null is necessary to match the behavior of AsmResolver.
    /// </remarks>
    public byte[]? PublicKeyToken
    {
        get
        {
            return publicKeyToken == default ? null : BitConverter.GetBytes(publicKeyToken);
        }
    }

    public string HashValue => LibCpp2IlMain.MetadataVersion > 24.3f ? "NULL" : LibCpp2IlMain.TheMetadata!.GetStringFromIndex(hashValueIndex);

    public override string ToString()
    {
        var pkt = string.Join("-", BitConverter.GetBytes(publicKeyToken).Select(b => b.ToString("X2")));
        return $"{Name}, Version={major}.{minor}.{build}.{revision}, PublicKeyToken={pkt}";
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        nameIndex = reader.ReadInt32();
        cultureIndex = reader.ReadInt32();
        if (IsAtMost(24.3f) && IsNot(24.15f))
            hashValueIndex = reader.ReadInt32();
        publicKeyIndex = reader.ReadInt32();
        hash_alg = reader.ReadUInt32();
        hash_len = reader.ReadInt32();
        flags = reader.ReadUInt32();
        major = reader.ReadInt32();
        minor = reader.ReadInt32();
        build = reader.ReadInt32();
        revision = reader.ReadInt32();
        publicKeyToken = reader.ReadUInt64();
    }
}
