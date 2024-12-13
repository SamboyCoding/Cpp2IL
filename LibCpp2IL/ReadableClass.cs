using System;

namespace LibCpp2IL;

public abstract class ReadableClass
{
    protected static bool IsAtLeast(float vers) => LibCpp2IlMain.MetadataVersion >= vers;
    protected static bool IsLessThan(float vers) => LibCpp2IlMain.MetadataVersion < vers;
    protected static bool IsAtMost(float vers) => LibCpp2IlMain.MetadataVersion <= vers;
    protected static bool IsNot(float vers) => Math.Abs(LibCpp2IlMain.MetadataVersion - vers) > 0.001f;
    protected static bool Is(float vers) => Math.Abs(LibCpp2IlMain.MetadataVersion - vers) < 0.001f;

    public abstract void Read(ClassReadingBinaryReader reader);
}
