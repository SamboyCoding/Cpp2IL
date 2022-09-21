using Arm64Disassembler.InternalDisassembly;
using Xunit.Abstractions;

namespace Arm64Disassembler.Tests;

public class LoadStoreTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public LoadStoreTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public void LoadStoreRegisterFromImm()
    {
        var raw = 0x38420F59U;
        
        var instruction = Disassembler.DisassembleSingleInstruction(raw);
        
        _testOutputHelper.WriteLine(instruction.ToString());
    }

    [Fact]
    public void LoadStoreRegFromRegOffset()
    {
        var raw = 0xB8697949U;
        
        var instruction = Disassembler.DisassembleSingleInstruction(raw);
        
        _testOutputHelper.WriteLine(instruction.ToString());
        
        Assert.Equal(Arm64Mnemonic.LDR, instruction.Mnemonic);
        Assert.Equal(Arm64Register.W9, instruction.Op0Reg);
        Assert.Equal(Arm64OperandKind.Memory, instruction.Op1Kind);
        Assert.Equal(Arm64Register.X10, instruction.MemBase);
        Assert.Equal(Arm64Register.X9, instruction.MemAddendReg);
        Assert.Equal(Arm64ShiftType.LSL, instruction.MemShiftType);
        Assert.Equal(2, instruction.MemExtendOrShiftAmount);
        
        Assert.Equal("0x00000000 LDR W9, [X10, X9, LSL #2]", instruction.ToString());
    }
}
