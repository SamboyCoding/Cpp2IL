using Xunit.Abstractions;

namespace Arm64Disassembler.Tests;

public class UnitTest1
{
    private readonly ITestOutputHelper _testOutputHelper;

    //Example Arm64 code for a basic assembly-level custom attribute generator
    /* Should disassemble to
         stp x20, x19, [sp, #-0x20]!
         stp x29, x30, [sp, #0x10]
         add x29, sp, #0x10
         mov x19, x0
         ldr x8, [x19, #8]
         mov x2, xzr
         movz w1, #0x102
         ldr x0, [x8]
         bl #0x13fc400
         ldr x8, [x19, #8]
         mov x1, xzr
         ldr x19, [x8, #8]
         mov x0, x19
         bl #0xf3d6cc
         ldp x29, x30, [sp, #0x10]
         mov x2, xzr
         orr w1, wzr, #1
         mov x0, x19
         ldp x20, x19, [sp], #0x20
         b #0xf3d6d4
    */
    private static byte[] caGenBody =
    {
        0xF4, 0x4F, 0xBE, 0xA9, 0xFD, 0x7B, 0x01, 0xA9, 0xFD, 0x43, 0x00, 0x91, 0xF3, 0x03, 0x00, 0xAA,
        0x68, 0x06, 0x40, 0xF9, 0xE2, 0x03, 0x1F, 0xAA, 0x41, 0x20, 0x80, 0x52, 0x00, 0x01, 0x40, 0xF9,
        0xF8, 0xF0, 0x4F, 0x94, 0x68, 0x06, 0x40, 0xF9, 0xE1, 0x03, 0x1F, 0xAA, 0x13, 0x05, 0x40, 0xF9,
        0xE0, 0x03, 0x13, 0xAA, 0xA6, 0xF5, 0x3C, 0x94, 0xFD, 0x7B, 0x41, 0xA9, 0xE2, 0x03, 0x1F, 0xAA,
        0xE1, 0x03, 0x00, 0x32, 0xE0, 0x03, 0x13, 0xAA, 0xF4, 0x4F, 0xC2, 0xA8, 0xA2, 0xF5, 0x3C, 0x14
    };

    /*
        stp x22, x21, [sp, #-0x30]!
        stp x20, x19, [sp, #0x10]
        stp x29, x30, [sp, #0x20]
        add x29, sp, #0x20
        sub sp, sp, #0x10
        adrp x21, #0x10cd000
     */
    private static byte[] IncludesPcRelAddressing =
    {
        0xF6, 0x57, 0xBD, 0xA9, 0xF4, 0x4F, 0x01, 0xA9, 0xFD, 0x7B, 0x02, 0xA9, 0xFD, 0x83, 0x00, 0x91,
        0xFF, 0x43, 0x00, 0xD1, 0x75, 0x86, 0x00, 0xB0
    };

    public UnitTest1(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public void TestDisassembleEntireBody()
    {
        var result = Disassembler.DisassembleOnDemand(IncludesPcRelAddressing, 0);

        foreach (var instruction in result)
        {
            _testOutputHelper.WriteLine(instruction.ToString());
        }
    }

    [Fact]
    public void LongTestForProfile()
    {
        var body = Enumerable.Repeat(caGenBody, 1000000).SelectMany(b => b).ToArray();

        var result = Disassembler.Disassemble(body, 0);
        
        Assert.Equal(body.Length / 4, result.Instructions.Count);
    }
}