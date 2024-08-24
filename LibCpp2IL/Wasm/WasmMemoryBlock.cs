using System.IO;
using System.Linq;

namespace LibCpp2IL.Wasm;

public class WasmMemoryBlock : ClassReadingBinaryReader
{
    internal byte[] Bytes;

    private static MemoryStream BuildStream(WasmFile file)
    {
        //Find the maximum byte in the data section that has a value
        var maxByte = file.DataSection.DataEntries
            .Where(s => s.VirtualOffset != ulong.MaxValue)
            .Select(s => s.VirtualOffset + s.Size)
            .Max();

        //Add an extra buffer beyond that just to be safe
        var toAlloc = (maxByte + 0x1000) * 2;
        var memoryBlock = new byte[toAlloc];
        var stream = new MemoryStream(memoryBlock, 0, (int)toAlloc, true, true);

        //Write from data segment
        foreach (var segment in file.DataSection.DataEntries)
        {
            stream.Seek((long)segment.VirtualOffset, SeekOrigin.Begin);
            stream.Write(segment.Data, 0, (int)segment.Size);
        }

        return stream;
    }

    public WasmMemoryBlock(WasmFile file) : base(BuildStream(file))
    {
        is32Bit = true;
        Bytes = ((MemoryStream)BaseStream).ToArray();
    }
}
