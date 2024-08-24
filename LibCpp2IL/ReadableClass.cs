using System;

namespace LibCpp2IL;

public abstract class ReadableClass
{
    protected bool IsAtLeast(float vers) => LibCpp2IlMain.MetadataVersion >= vers;
    protected bool IsLessThan(float vers) => LibCpp2IlMain.MetadataVersion < vers;
    protected bool IsAtMost(float vers) => LibCpp2IlMain.MetadataVersion <= vers;
    protected bool IsNot(float vers) => Math.Abs(LibCpp2IlMain.MetadataVersion - vers) > 0.001f;

    public abstract void Read(ClassReadingBinaryReader reader);
}
