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
}
