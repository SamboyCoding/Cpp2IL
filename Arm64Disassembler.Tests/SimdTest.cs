using Arm64Disassembler.InternalDisassembly;
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

    [Fact]
    public void Test2SourceFp()
    {
        var raw = 0x1E201820U;
        
        var result = Disassembler.DisassembleSingleInstruction(raw);
        
        _testOutputHelper.WriteLine(result.ToString());
        
        Assert.Equal(Arm64Mnemonic.FDIV, result.Mnemonic);
    }

    [Fact]
    public void TestFp16Scvtf()
    {
        var raw = 0x5E21D800U;
        
        var result = Disassembler.DisassembleSingleInstruction(raw);
        
        _testOutputHelper.WriteLine(result.ToString());
        
        Assert.Equal(Arm64Mnemonic.SCVTF, result.Mnemonic);
        Assert.Equal(Arm64Register.S0, result.Op0Reg);
    }
}
