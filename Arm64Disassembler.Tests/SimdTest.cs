using Xunit.Abstractions;

namespace Arm64Disassembler.Tests;

public class SimdTest
{
    private readonly ITestOutputHelper _testOutputHelper;

    public SimdTest(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public void TestSimdInstruction()
    {
        var insn = (uint) 0x4EA0_1C08;

        var result = Disassembler.DisassembleSingleInstruction(insn);
        
        _testOutputHelper.WriteLine(result.ToString());
        
        Assert.Equal(Arm64Mnemonic.MOV, result.Mnemonic);
    }

    [Fact]
    public void TestScvtf()
    {
        var raw = 0x1E2202A1U;
        
        var result = Disassembler.DisassembleSingleInstruction(raw);
        
        _testOutputHelper.WriteLine(result.ToString());
        
        Assert.Equal(Arm64Mnemonic.SCVTF, result.Mnemonic);
    }
}
