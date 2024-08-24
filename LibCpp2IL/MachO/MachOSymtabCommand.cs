using System;
using System.Collections.Generic;

namespace LibCpp2IL.MachO;

public class MachOSymtabCommand : ReadableClass
{
    public uint SymbolTableOffset;
    public uint NumSymbols;
    public uint StringTableOffset;
    public uint StringTableSize;

    public MachOSymtabEntry[] Symbols = [];

    public override void Read(ClassReadingBinaryReader reader)
    {
        SymbolTableOffset = reader.ReadUInt32();
        NumSymbols = reader.ReadUInt32();
        StringTableOffset = reader.ReadUInt32();
        StringTableSize = reader.ReadUInt32();

        var returnTo = reader.BaseStream.Position;

        reader.BaseStream.Position = SymbolTableOffset;

        Symbols = new MachOSymtabEntry[NumSymbols];
        for (var i = 0; i < NumSymbols; i++)
        {
            Symbols[i] = new();
            Symbols[i].Read(reader, this);
        }

        reader.BaseStream.Position = returnTo;
    }
}
