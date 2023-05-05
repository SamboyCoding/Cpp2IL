using Cpp2IL.Core.Il2CppApiFunctions;
using Disarm;
using LibCpp2IL;

namespace Cpp2IL.InstructionSets.ArmV8;

public class ArmV8KeyFunctionAddresses : BaseKeyFunctionAddresses
{
    private Arm64DisassemblyResult? _cachedDisassembledBytes;

    private Arm64DisassemblyResult DisassembleTextSection()
    {
        if (_cachedDisassembledBytes == null)
        {
            var toDisasm = LibCpp2IlMain.Binary!.GetEntirePrimaryExecutableSection();
            _cachedDisassembledBytes = Disassembler.Disassemble(toDisasm, LibCpp2IlMain.Binary.GetVirtualAddressOfPrimaryExecutableSection());
        }

        return _cachedDisassembledBytes.Value;
    }

    protected override IEnumerable<ulong> FindAllThunkFunctions(ulong addr, uint maxBytesBack = 0, params ulong[] addressesToIgnore)
    {
        //Disassemble .text
        var disassembly = DisassembleTextSection();

        //Find all jumps to the target address
        var matchingJmps = disassembly.Instructions.Where(i => i.Mnemonic is Arm64Mnemonic.B or Arm64Mnemonic.BL && i.BranchTarget == addr).ToList();

        foreach (var matchingJmp in matchingJmps)
        {
            if (addressesToIgnore.Contains(matchingJmp.Address)) continue;

            //Find this instruction in the raw file
            var offsetInPe = (ulong) LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(matchingJmp.Address);
            if (offsetInPe == 0 || offsetInPe == (ulong) (LibCpp2IlMain.Binary.RawLength - 1))
                continue;

            //get next and previous bytes
            var previousByte = LibCpp2IlMain.Binary.GetByteAtRawAddress(offsetInPe - 1);
            var nextByte = LibCpp2IlMain.Binary.GetByteAtRawAddress(offsetInPe + 4);

            //Double-cc = thunk
            if (previousByte == 0xCC && nextByte == 0xCC)
            {
                yield return matchingJmp.Address;
                continue;
            }

            if (nextByte == 0xCC && maxBytesBack > 0)
            {
                for (ulong backtrack = 1; backtrack < maxBytesBack && offsetInPe - backtrack > 0; backtrack++)
                {
                    if (addressesToIgnore.Contains(matchingJmp.Address - (backtrack - 1)))
                        //Move to next jmp
                        break;

                    if (LibCpp2IlMain.Binary.GetByteAtRawAddress(offsetInPe - backtrack) == 0xCC)
                    {
                        yield return matchingJmp.Address - (backtrack - 1);
                        break;
                    }
                }
            }
        }
    }

    protected override ulong GetObjectIsInstFromSystemType()
    {
        throw new NotImplementedException();
    }

    protected override ulong FindFunctionThisIsAThunkOf(ulong thunkPtr, bool prioritiseCall = false)
    {
        throw new NotImplementedException();
    }

    protected override int GetCallerCount(ulong toWhere)
    {
        //Disassemble .text
        var disassembly = DisassembleTextSection();

        //Find all jumps to the target address
        return disassembly.Instructions.Count(i => i.Mnemonic is Arm64Mnemonic.B or Arm64Mnemonic.BL && i.BranchTarget == toWhere);
    }
}
