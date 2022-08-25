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
    }
}
