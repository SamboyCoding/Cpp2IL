using System;

namespace LibCpp2IL.MachO;

public class MachODynamicLinkerCommand : ReadableClass
{
    public int RebaseOffset;
    public int RebaseSize;
    public int BindOffset;
    public int BindSize;
    public int WeakBindOffset;
    public int WeakBindSize;
    public int LazyBindOffset;
    public int LazyBindSize;
    public int ExportOffset;
    public int ExportSize;

    public MachOExportEntry[] Exports = [];

    public override void Read(ClassReadingBinaryReader reader)
    {
        RebaseOffset = reader.ReadInt32();
        RebaseSize = reader.ReadInt32();
        BindOffset = reader.ReadInt32();
        BindSize = reader.ReadInt32();
        WeakBindOffset = reader.ReadInt32();
        WeakBindSize = reader.ReadInt32();
        LazyBindOffset = reader.ReadInt32();
        LazyBindSize = reader.ReadInt32();
        ExportOffset = reader.ReadInt32();
        ExportSize = reader.ReadInt32();

        var returnTo = reader.BaseStream.Position;

        reader.BaseStream.Position = ExportOffset;

        var exports = new MachOExportTrie(reader);
        Exports = exports.Entries.ToArray();

        reader.BaseStream.Position = returnTo;
    }
}
