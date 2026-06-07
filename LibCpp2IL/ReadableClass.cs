using System;

namespace LibCpp2IL;

public abstract class ReadableClass
{
    private LibCpp2IlContext? _owningContext;

    internal float MetadataVersion { get; set; }

    internal LibCpp2IlContext OwningContext
    {
        get => _owningContext ?? throw new InvalidOperationException("OwningContext has not been initialized.");
        set => _owningContext = value;
    }

    protected bool IsAtLeast(float vers) => MetadataVersion >= vers;
    protected bool IsLessThan(float vers) => MetadataVersion < vers;
    protected bool IsAtMost(float vers) => MetadataVersion <= vers;
    protected bool IsNot(float vers) => Math.Abs(MetadataVersion - vers) > 0.001f;
    protected bool Is(float vers) => Math.Abs(MetadataVersion - vers) < 0.001f;

    public abstract void Read(ClassReadingBinaryReader reader);
}
