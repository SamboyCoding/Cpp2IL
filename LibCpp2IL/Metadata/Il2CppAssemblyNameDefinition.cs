using System;
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

    public string PublicKey => LibCpp2IlMain.TheMetadata!.GetStringFromIndex(publicKeyIndex);

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
