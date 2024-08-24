using System;
using System.Collections.Generic;

namespace LibCpp2IL.MachO;

public class MachOExportTrie
{
    public List<MachOExportEntry> Entries = [];

    private ClassReadingBinaryReader _reader;
    private long _basePtr;

    public MachOExportTrie(ClassReadingBinaryReader reader)
    {
        _reader = reader;
        _basePtr = reader.BaseStream.Position;

        var children = ParseNode("", 0);
        while (children.Count > 0)
        {
            var current = children[0];
            children.RemoveAt(0);
            children.AddRange(ParseNode(current.Name, current.Offset));
        }
    }

    private List<Node> ParseNode(string name, int offset)
    {
        var children = new List<Node>();
        _reader.BaseStream.Position = _basePtr + offset;

        var terminalSize = _reader.BaseStream.ReadLEB128Unsigned();
        var childrenIndex = _reader.BaseStream.Position + (long)terminalSize;
        if (terminalSize != 0)
        {
            var flags = (ExportFlags)_reader.BaseStream.ReadLEB128Unsigned();
            var address = 0L;
            var other = 0L;
            string? importName = null;

            if ((flags & ExportFlags.ReExport) != 0)
            {
                other = _reader.BaseStream.ReadLEB128Signed();
                importName = _reader.ReadStringToNullAtCurrentPos();
            }
            else
            {
                address = _reader.BaseStream.ReadLEB128Signed();
                if ((flags & ExportFlags.StubAndResolver) != 0)
                    other = _reader.BaseStream.ReadLEB128Signed();
            }

            Entries.Add(new(name, address, (long)flags, other, importName));
        }

        _reader.BaseStream.Position = childrenIndex;
        var numChildren = _reader.BaseStream.ReadLEB128Unsigned();
        for (var i = 0ul; i < numChildren; i++)
        {
            var childName = _reader.ReadStringToNullAtCurrentPos();
            var childOffset = _reader.BaseStream.ReadLEB128Unsigned();
            children.Add(new Node { Name = name + childName, Offset = (int)childOffset });
        }

        return children;
    }

    [Flags]
    private enum ExportFlags
    {
        KindRegular = 0,
        KindThreadLocal = 1,
        KindAbsolute = 2,
        WeakDefinition = 4,
        ReExport = 8,
        StubAndResolver = 0x10
    }

    private struct Node
    {
        public string Name;
        public int Offset;
    }
}
