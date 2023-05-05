using CapstoneSharp.Arm;
using Cpp2IL.Core.Il2CppApiFunctions;
using Cpp2IL.Core.Logging;
using LibCpp2IL;
using LibCpp2IL.Reflection;

namespace Cpp2IL.InstructionSets.ArmV7;

public class ArmV7KeyFunctionAddresses : BaseKeyFunctionAddresses
{
    private List<CapstoneArmInstruction>? _cachedDisassembledBytes;

    private List<CapstoneArmInstruction> DisassembleTextSection()
    {
        if (_cachedDisassembledBytes == null)
        {
            var toDisasm = LibCpp2IlMain.Binary!.GetEntirePrimaryExecutableSection();
            _cachedDisassembledBytes = ArmV7Utils.Disassembler.Iterate(toDisasm, LibCpp2IlMain.Binary.GetVirtualAddressOfPrimaryExecutableSection()).ToList();
        }

        return _cachedDisassembledBytes;
    }

    protected override IEnumerable<ulong> FindAllThunkFunctions(ulong addr, uint maxBytesBack = 0, params ulong[] addressesToIgnore)
    {
        //Disassemble .text
        var disassembly = DisassembleTextSection();

        //Find all jumps to the target address
        var matchingJmps = disassembly.Where(i => i.IsBranchingTo((int)addr)).ToList();

        foreach (var matchingJmp in matchingJmps)
        {
            if (addressesToIgnore.Contains(matchingJmp.Address)) continue;

            var backtrack = 0;
            var idx = disassembly.IndexOf(matchingJmp);

            do
            {
                //About the only way we have of checking for a thunk is if there is an all-zero instruction or another unconditional branch just before this
                //Or a ret, but that's less reliable.
                //if so, this is probably a thunk.
                if (idx - backtrack > 0)
                {
                    var prevInstruction = disassembly[idx - backtrack - 1];

                    if (addressesToIgnore.Contains(prevInstruction.Address))
                    {
                        backtrack++;
                        continue;
                    }

                    if (prevInstruction.IsSkippedData && prevInstruction.Bytes.IsAllZero())
                    {
                        //All-zero instructions are a match
                        yield return matchingJmp.Address - (ulong)(backtrack * 4);
                        break;
                    }

                    if (prevInstruction.Id is CapstoneArmInstructionId.STR)
                    {
                        //ADRP instructions are a deal breaker - this means we're loading something from memory, so it's not a simple thunk
                        break;
                    }

                    if (prevInstruction.Id is CapstoneArmInstructionId.B or CapstoneArmInstructionId.BL)
                    {
                        //Previous branches are a match
                        yield return matchingJmp.Address - (ulong)(backtrack * 4);
                        break;
                    }
                }

                //We're working in the .text section here so we have few symbols, so there's no point looking for the previous one.

                backtrack++;
            } while (backtrack * 4 < maxBytesBack);
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
        var instructions = ArmV7Utils.DisassembleManagedMethod(typeIsInstanceOfType.MethodPointer);

        var lastCall = instructions.LastOrDefault(i => i.Id == CapstoneArmInstructionId.BL);

        if (lastCall == null)
        {
            Logger.VerboseNewline("Method does not match expected signature. Aborting.");
            return 0;
        }

        var target = lastCall.GetBranchTarget();
        Logger.VerboseNewline($"Success. IsInst found at 0x{target:X}");
        return (ulong)target;
    }

    protected override ulong FindFunctionThisIsAThunkOf(ulong thunkPtr, bool prioritiseCall = false)
    {
        var instructions = ArmV7Utils.DisassembleFunction(thunkPtr);

        try
        {
            var target = prioritiseCall ? CapstoneArmInstructionId.BL : CapstoneArmInstructionId.B;
            var matchingCall = instructions.FirstOrDefault(i => i.Id == target);

            if (matchingCall == null)
            {
                target = target == CapstoneArmInstructionId.BL ? CapstoneArmInstructionId.B : CapstoneArmInstructionId.BL;
                matchingCall = instructions.First(i => i.Id == target);
            }

            return (ulong)matchingCall.GetBranchTarget();
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
        return disassembly.Count(i => i.IsBranchingTo((int)toWhere));
    }
}
