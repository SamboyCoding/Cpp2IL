using System.Linq;

namespace LibCpp2IL.Metadata;

public class Il2CppImageDefinition : ReadableClass
{
    public int nameIndex;
    public int assemblyIndex;

    public Il2CppVariableWidthIndex<Il2CppTypeDefinition> firstTypeIndex;
    public uint typeCount;

    [Version(Min = 24)] public Il2CppVariableWidthIndex<Il2CppTypeDefinition> exportedTypeStart;
    [Version(Min = 24)] public uint exportedTypeCount;

    public Il2CppVariableWidthIndex<Il2CppMethodDefinition> entryPointIndex;
    public uint token;

    [Version(Min = 24.1f)] public int customAttributeStart;
    [Version(Min = 24.1f)] public uint customAttributeCount;

    public string? Name => OwningContext.Metadata.GetStringFromIndex(nameIndex);

    public Il2CppTypeDefinition[] Types => Enumerable
            .Range(firstTypeIndex.Value, (int)typeCount)
            .Select(Il2CppVariableWidthIndex<Il2CppTypeDefinition>.MakeTemporaryForFixedWidthUsage) // DynWidth: using Enumerable.Range, not read from file, so making temp is ok
            .Select(OwningContext.Metadata.GetTypeDefinitionFromIndex)
            .ToArray();

    public Il2CppTypeDefinition[]? ExportedTypes => IsAtLeast(24)
        ? Enumerable
            .Range(exportedTypeStart.Value, (int)exportedTypeCount)
            .Select(OwningContext.Metadata.GetExportedTypeDefintionFromIndex)
            .ToArray()
        : null;

    public override string ToString()
    {
        return $"Il2CppImageDefinition[Name={Name}]";
    }

    public override void Read(ClassReadingBinaryReader reader)
    {
        nameIndex = reader.ReadInt32();
        assemblyIndex = reader.ReadInt32();     

        firstTypeIndex = Il2CppVariableWidthIndex<Il2CppTypeDefinition>.Read(reader);
        typeCount = reader.ReadUInt32();

        if (IsAtLeast(24f))
        {
            exportedTypeStart = Il2CppVariableWidthIndex<Il2CppTypeDefinition>.Read(reader);
            exportedTypeCount = reader.ReadUInt32();
        }

        entryPointIndex = Il2CppVariableWidthIndex<Il2CppMethodDefinition>.Read(reader);
        token = reader.ReadUInt32();

        if (IsAtLeast(24.1f))
        {
            customAttributeStart = reader.ReadInt32();
            customAttributeCount = reader.ReadUInt32();
        }
    }
}
