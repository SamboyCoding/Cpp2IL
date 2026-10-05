using System.IO;
using LibCpp2IL.MachO;

namespace Cpp2IL.Core.Tests;

public class MachOUniversalFileTests
{
    // Virtual addresses of fixture_first and fixture_second, the two self-referencing pointers in
    // the x86_64 slice's __DATA,__data section (see TestFiles/MachO/ChainedFixupsUniversal.c).
    private const ulong FirstPointerAddress = 0x1000;
    private const ulong SecondPointerAddress = 0x1008;

    [Test]
    public void ChainedFixupsAreAppliedToTheSelectedSlice()
    {
        var file = new MachOUniversalFile(new MemoryStream(File.ReadAllBytes(Paths.MachO.ChainedFixupsUniversal)));

        // On disk the first pointer is a chained rebase that also encodes the offset to the next
        // one, so it only reads as its own address once the fixups have been applied.
        using (Assert.EnterMultipleScope())
        {
            Assert.That(file.BestMachOFile.ReadPointerAtVirtualAddress(FirstPointerAddress), Is.EqualTo(FirstPointerAddress));
            Assert.That(file.BestMachOFile.ReadPointerAtVirtualAddress(SecondPointerAddress), Is.EqualTo(SecondPointerAddress));
        }
    }
}
