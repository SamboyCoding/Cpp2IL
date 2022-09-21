using System.Reflection;
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

    [Fact]
    public void TestFpCompare()
    {
        var raw = 0x1E602020U;
        
        var result = Disassembler.DisassembleSingleInstruction(raw);
        
        _testOutputHelper.WriteLine(result.ToString());
        
        Assert.Equal(Arm64Mnemonic.FCMP, result.Mnemonic);
        Assert.Equal(Arm64Register.D1, result.Op0Reg);
        Assert.Equal(Arm64Register.D0, result.Op1Reg);
    }

    [Fact]
    public void TestFsqrt()
    {
        var raw = 0x1E61C020U;
        
        var result = Disassembler.DisassembleSingleInstruction(raw);
        
        _testOutputHelper.WriteLine(result.ToString());
        
        Assert.Equal(Arm64Mnemonic.FSQRT, result.Mnemonic);
        Assert.Equal(Arm64Register.D0, result.Op0Reg);
        Assert.Equal(Arm64Register.D1, result.Op1Reg);
    }
}
