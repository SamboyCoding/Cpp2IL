using Arm64Disassembler.InternalDisassembly;
using Xunit.Abstractions;

namespace Arm64Disassembler.Tests;

public class DataProcessingTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public DataProcessingTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }
    
    [Fact]
    public void DisassemblingBitfieldsWorks()
    {
        var raw = 0x93407E95;
        var insn = Disassembler.DisassembleSingleInstruction(raw);
        
        _testOutputHelper.WriteLine(insn.ToString());
        
        Assert.Equal(Arm64Mnemonic.SXTW, insn.Mnemonic);
        Assert.Equal(Arm64Register.X21, insn.Op0Reg);
        Assert.Equal(Arm64Register.W20, insn.Op1Reg);
    }

    [Fact]
    public void DataProcessing2Source()
    {
        var raw = 0x1AC80D2AU;
        
        var insn = Disassembler.DisassembleSingleInstruction(raw);
        
        _testOutputHelper.WriteLine(insn.ToString());
        
        Assert.Equal(Arm64Mnemonic.SDIV, insn.Mnemonic);
    }
}
