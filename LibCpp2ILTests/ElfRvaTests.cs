using System;
using System.IO;
using LibCpp2IL.Elf;
using Xunit;

namespace LibCpp2ILTests;

public class ElfRvaTests
{
    // Minimal little-endian ELF64 aarch64 with two PT_LOADs whose virtual addresses are 0x4000 above their file offsets (16 KB aligned build).
    private static ElfFile BuildElf(ulong imageBase)
    {
        var buf = new byte[0x2000];
        var w = new BinaryWriter(new MemoryStream(buf));
        w.Write(0x464c457fu); w.Write((byte)2); w.Write((byte)1); w.Write((byte)1); // magic, 64-bit, LE, version
        w.Seek(0x10, SeekOrigin.Begin);
        w.Write((short)3); w.Write((short)0xB7); w.Write(1); // type, machine, version
        w.Write(0UL); w.Write(0x40L); w.Write(0L); w.Write(0); // entry, phoff, shoff, flags
        w.Write((short)0x40); w.Write((short)0x38); w.Write((short)3); // ehsize, phentsize, phnum
        w.Write((short)0); w.Write((short)0); w.Write((short)0); // shentsize, shnum, shstrndx
        WritePhdr(w, 0, 0x1000, imageBase, 4); // R: starts at the image base
        WritePhdr(w, 0x1000, 0x1000, imageBase + 0x5000, 5); // R E: file offset 0x1000, vaddr 0x4000 higher
        WritePhdr(w, 0x1800, 0x800, imageBase + 0x5800, 6); // RW
        w.Flush();
        return new ElfFile(new MemoryStream(buf, 0, buf.Length, false, true));
    }

    private static void WritePhdr(BinaryWriter w, ulong offset, ulong size, ulong vaddr, uint flags)
    {
        w.Write(1u); w.Write(flags); w.Write(offset); w.Write(vaddr); w.Write(vaddr); w.Write(size); w.Write(size); w.Write(0x1000L);
    }

    [Fact]
    public void GetRva_ReturnsVirtualAddressMinusImageBase_NotFileOffset()
    {
        var elf = BuildElf(0);
        Assert.Equal(0x5234UL, elf.GetRva(0x5234));
        Assert.Equal(0x5810UL, elf.GetRva(0x5810));
    }

    [Fact]
    public void GetRva_SubtractsNonZeroImageBase()
    {
        var elf = BuildElf(0x400000);
        Assert.Equal(0x5234UL, elf.GetRva(0x405234));
    }
}
