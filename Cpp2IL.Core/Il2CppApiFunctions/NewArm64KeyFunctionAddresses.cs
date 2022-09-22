using System;
using System.Collections.Generic;
using System.Linq;
using Disarm;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Il2CppApiFunctions;

public class NewArm64KeyFunctionAddresses : BaseKeyFunctionAddresses
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
        Logger.Verbose("\tTrying to use System.Type::IsInstanceOfType to find il2cpp::vm::Object::IsInst...");
        var typeIsInstanceOfType = LibCpp2IlReflection.GetType("Type", "System")?.Methods?.FirstOrDefault(m => m.Name == "IsInstanceOfType");
        if (typeIsInstanceOfType == null)
        {
            Logger.VerboseNewline("Type or method not found, aborting.");
            return 0;
        }
            
        //IsInstanceOfType is a very simple ICall, that looks like this:
        //  Il2CppClass* klass = vm::Class::FromIl2CppType(type->type.type);
        //  return il2cpp::vm::Object::IsInst(obj, klass) != NULL;
        //The last call is to Object::IsInst
            
        Logger.Verbose($"IsInstanceOfType found at 0x{typeIsInstanceOfType.MethodPointer:X}...");
        var instructions = X86Utils.GetMethodBodyAtVirtAddressNew(typeIsInstanceOfType.MethodPointer, true);

        var lastCall = instructions.LastOrDefault(i => i.Mnemonic == Mnemonic.Call);

        if (lastCall.Mnemonic == Mnemonic.INVALID)
        {
            Logger.VerboseNewline("Method does not match expected signature. Aborting.");
            return 0;
        }
            
        Logger.VerboseNewline($"Success. IsInst found at 0x{lastCall.NearBranchTarget:X}");
        return lastCall.NearBranchTarget;
    }

    protected override ulong FindFunctionThisIsAThunkOf(ulong thunkPtr, bool prioritiseCall = false)
    {
        var instructions = X86Utils.GetMethodBodyAtVirtAddressNew(thunkPtr, true);

        try
        {
            var target = prioritiseCall ? Mnemonic.Call : Mnemonic.Jmp;
            var matchingCall = instructions.FirstOrDefault(i => i.Mnemonic == target);

            if (matchingCall.Mnemonic == Mnemonic.INVALID)
            {
                target = target == Mnemonic.Call ? Mnemonic.Jmp : Mnemonic.Call;
                matchingCall = instructions.First(i => i.Mnemonic == target);
            }

            return matchingCall.NearBranchTarget;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    protected override int GetCallerCount(ulong toWhere)
    {
        //Disassemble .text
        var disassembly = DisassembleTextSection();

        //Find all jumps to the target address
        return disassembly.Instructions.Count(i => i.Mnemonic is Arm64Mnemonic.B or Arm64Mnemonic.BL && i.BranchTarget == toWhere);
    }
}
