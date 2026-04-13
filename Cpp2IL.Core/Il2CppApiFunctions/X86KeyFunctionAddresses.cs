using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Iced.Intel;

namespace Cpp2IL.Core.Il2CppApiFunctions;

public class X86KeyFunctionAddresses : BaseKeyFunctionAddresses
{
    private InstructionList? _cachedDisassembledBytes;

    private InstructionList DisassembleTextSection()
    {
        if (_cachedDisassembledBytes == null)
        {
            var binary = _appContext.Binary;
            var toDisasm = binary.GetEntirePrimaryExecutableSection();
            _cachedDisassembledBytes = X86Utils.Disassemble(toDisasm, binary.GetVirtualAddressOfPrimaryExecutableSection(), binary.is32Bit);
        }

        return _cachedDisassembledBytes;
    }

    public override void Find(ApplicationAnalysisContext applicationAnalysisContext)
    {
        base.Find(applicationAnalysisContext);

        _cachedDisassembledBytes = null; //Clean up once we're done finding everything
    }

    protected override IEnumerable<ulong> FindAllThunkFunctions(ulong addr, uint maxBytesBack = 0, params ulong[] addressesToIgnore)
    {
        //Disassemble .text
        var allInstructions = DisassembleTextSection();

        //Find all jumps to the target address
        var matchingJmps = allInstructions.Where(i => i.Mnemonic is Mnemonic.Jmp or Mnemonic.Call && i.NearBranchTarget == addr).ToList();

        foreach (var matchingJmp in matchingJmps)
        {
            if (addressesToIgnore.Contains(matchingJmp.IP)) continue;

            //Find this instruction in the raw file
            var binary = _appContext.Binary;
            var offsetInPe = (ulong)binary.MapVirtualAddressToRaw(matchingJmp.IP);
            if (offsetInPe == 0 || offsetInPe == (ulong)(binary.RawLength - 1))
                continue;

            //get next and previous bytes
            var previousByte = binary.GetByteAtRawAddress(offsetInPe - 1);
            var nextByte = binary.GetByteAtRawAddress(offsetInPe + (ulong)matchingJmp.Length);

            //Double-cc = thunk
            if (previousByte == 0xCC && nextByte == 0xCC)
            {
                yield return matchingJmp.IP;
                continue;
            }

            if (nextByte == 0xCC && maxBytesBack > 0)
            {
                for (ulong backtrack = 1; backtrack < maxBytesBack && offsetInPe - backtrack > 0; backtrack++)
                {
                    if (addressesToIgnore.Contains(matchingJmp.IP - (backtrack - 1)))
                        //Move to next jmp
                        break;

                    if (binary.GetByteAtRawAddress(offsetInPe - backtrack) == 0xCC)
                    {
                        yield return matchingJmp.IP - (backtrack - 1);
                        break;
                    }
                }
            }
        }
    }

    protected override ulong GetObjectIsInstFromSystemType()
    {
        Logger.Verbose("\tTrying to use System.Type::IsInstanceOfType to find il2cpp::vm::Object::IsInst...");
        var typeIsInstanceOfType = ReflectionCache.GetType("Type", "System")?.Methods?.FirstOrDefault(m => m.Name == "IsInstanceOfType");
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
        var instructions = X86Utils.GetMethodBodyAtVirtAddressNew(typeIsInstanceOfType.MethodPointer, true, _appContext.Binary);

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
        var instructions = X86Utils.GetMethodBodyAtVirtAddressNew(thunkPtr, true, _appContext.Binary);

        var target = prioritiseCall ? Mnemonic.Call : Mnemonic.Jmp;
        var matchingCall = instructions.FirstOrDefault(i => i.Mnemonic == target);

        if (matchingCall.Mnemonic == Mnemonic.INVALID)
        {
            target = target == Mnemonic.Call ? Mnemonic.Jmp : Mnemonic.Call;
            matchingCall = instructions.FirstOrDefault(i => i.Mnemonic == target);
        }

        return matchingCall.Mnemonic != Mnemonic.INVALID ? matchingCall.NearBranchTarget : 0;
    }

    protected override int GetCallerCount(ulong toWhere)
    {
        //Disassemble .text
        var allInstructions = DisassembleTextSection();

        //Find all jumps to the target address
        return allInstructions.Count(i => i.Mnemonic == Mnemonic.Jmp || i.Mnemonic == Mnemonic.Call && i.NearBranchTarget == toWhere);
    }
}
