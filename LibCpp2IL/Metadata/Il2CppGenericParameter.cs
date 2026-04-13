using System.Linq;
using System.Reflection;
using LibCpp2IL.BinaryStructures;

namespace LibCpp2IL.Metadata;

public class Il2CppGenericParameter : ReadableClass
{
    public Il2CppVariableWidthIndex<Il2CppGenericContainer> ownerIndex; /* Type or method this parameter was defined in. */
    public int nameIndex;
    public short constraintsStart;
    public short constraintsCount;
    public ushort genericParameterIndexInOwner;
    public ushort flags;

    public GenericParameterAttributes Attributes => (GenericParameterAttributes)flags;

    public string? Name => OwningContext.Metadata.GetStringFromIndex(nameIndex);

    public Il2CppType[] ConstraintTypes => constraintsCount == 0
        ? []
        : OwningContext.Metadata.constraintIndices
            .Skip(constraintsStart)
            .Take(constraintsCount)
            .Select(OwningContext.Binary.GetType)
            .ToArray();

    /// <summary>
    /// The index of this generic parameter to be passed to <see cref="Il2CppMetadata.GetGenericParameterFromIndex"/> to obtain this instance
    /// </summary>
    public Il2CppVariableWidthIndex<Il2CppGenericParameter> Index { get; internal set; }

    public Il2CppGenericContainer Owner => OwningContext.Metadata.GetGenericContainerFromIndex(ownerIndex);

    public Il2CppTypeEnum Type => Owner.isGenericMethod ? Il2CppTypeEnum.IL2CPP_TYPE_MVAR : Il2CppTypeEnum.IL2CPP_TYPE_VAR;

    public override void Read(ClassReadingBinaryReader reader)
    {
        ownerIndex = Il2CppVariableWidthIndex<Il2CppGenericContainer>.Read(reader);
        nameIndex = reader.ReadInt32();
        constraintsStart = reader.ReadInt16();
        constraintsCount = reader.ReadInt16();
        genericParameterIndexInOwner = reader.ReadUInt16();
        flags = reader.ReadUInt16();
    }
}
