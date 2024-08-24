using System;
using System.Linq;
using LibCpp2IL.BinaryStructures;

namespace LibCpp2IL.Metadata;

public class Il2CppGenericParameter : ReadableClass
{
    public int ownerIndex; /* Type or method this parameter was defined in. */
    public int nameIndex;
    public short constraintsStart;
    public short constraintsCount;
    public ushort genericParameterIndexInOwner;
    public ushort flags;

    public string? Name => LibCpp2IlMain.TheMetadata?.GetStringFromIndex(nameIndex);

    public Il2CppType[]? ConstraintTypes => constraintsCount == 0
        ? []
        : LibCpp2IlMain.TheMetadata?.constraintIndices
            .Skip(constraintsStart)
            .Take(constraintsCount)
            .Select(LibCpp2IlMain.Binary!.GetType)
            .ToArray();

    public int Index { get; internal set; }

    public override void Read(ClassReadingBinaryReader reader)
    {
        ownerIndex = reader.ReadInt32();
        nameIndex = reader.ReadInt32();
        constraintsStart = reader.ReadInt16();
        constraintsCount = reader.ReadInt16();
        genericParameterIndexInOwner = reader.ReadUInt16();
        flags = reader.ReadUInt16();
    }
}
