using System;
using System.Collections.Generic;
using System.Linq;
using Disarm;
using Disarm.InternalDisassembly;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.Il2CppApiFunctions;

public class NewArm64KeyFunctionAddresses : BaseKeyFunctionAddresses
{
    private static readonly (string Namespace, string Type, string Method)[] WriteBarrierAnchors =
    [
        ("System.Threading.Tasks", "Task`1", "GetAwaiter"),
        ("System.Threading.Tasks", "Task", "GetAwaiter"),
        ("System.Threading", "ExecutionContext", "get_LogicalCallContext"),
        ("System.Threading", "CancellationTokenSource", "get_Token"),
        ("System", "BadImageFormatException", "get_Message"),
    ];

    private List<Arm64Instruction>? _cachedDisassembledBytes;

    private List<Arm64Instruction> DisassembleTextSection()
    {
        if (_cachedDisassembledBytes == null)
        {
            var binary = _appContext.Binary;
            var toDisasm = binary.GetEntirePrimaryExecutableSection();
            _cachedDisassembledBytes = Disassembler.Disassemble(toDisasm, binary.GetVirtualAddressOfPrimaryExecutableSection(), new(true, true, false)).ToList();
        }

        return _cachedDisassembledBytes;
    }

    private HashSet<ulong> CallTargets => field ??=
    [
        .. DisassembleTextSection()
            .Where(i => i.Mnemonic == Arm64Mnemonic.BL)
            .Select(i => i.BranchTarget)
    ];

    private bool IsFunctionStart(List<Arm64Instruction> disassembly, int index)
    {
        if (CallTargets.Contains(disassembly[index].Address))
            return true;

        if (index == 0)
            return true;

        // it's a function start if the previous instruction can't fall through into it
        var previous = disassembly[index - 1];
        return previous.Mnemonic is Arm64Mnemonic.RET or Arm64Mnemonic.RETAA or Arm64Mnemonic.RETAB or Arm64Mnemonic.BR or Arm64Mnemonic.BRK or Arm64Mnemonic.INVALID
               || (previous.Mnemonic == Arm64Mnemonic.B && previous.MnemonicConditionCode is Arm64ConditionCode.NONE or Arm64ConditionCode.AL);
    }

    protected override IEnumerable<ulong> FindAllThunkFunctions(ulong addr, uint maxBytesBack = 0, params ulong[] addressesToIgnore)
    {
        //Disassemble .text
        var disassembly = DisassembleTextSection();

        for (var index = 0; index < disassembly.Count; index++)
        {
            var instruction = disassembly[index];

            // a thunk ends by tail-calling the real function
            if (instruction.Mnemonic != Arm64Mnemonic.B || instruction.MnemonicConditionCode is not (Arm64ConditionCode.NONE or Arm64ConditionCode.AL) || instruction.BranchTarget != addr)
                continue;

            if (addressesToIgnore.Contains(instruction.Address))
                continue;

            // walk back over any setup instructions to the start of the function containing the branch,
            // bailing if it's too far away to be a thunk
            var maxInstructionsBack = (int)(maxBytesBack / 4);
            for (var back = 0; back <= maxInstructionsBack && index - back >= 0; back++)
            {
                if (!IsFunctionStart(disassembly, index - back))
                    continue;

                var start = disassembly[index - back].Address;
                if (!addressesToIgnore.Contains(start))
                    yield return start;

                break;
            }
        }
    }

    protected override ulong FindFirstCallTargetInMethod(ulong methodVa)
    {
        var instructions = NewArm64Utils.GetArm64MethodBodyAtVirtualAddress(_appContext, methodVa, false);
        var call = instructions.FirstOrDefault(i => i.Mnemonic == Arm64Mnemonic.BL);
        return call.Mnemonic == Arm64Mnemonic.BL ? call.BranchTarget : 0;
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
        var instructions = NewArm64Utils.GetArm64MethodBodyAtVirtualAddress(_appContext, typeIsInstanceOfType.MethodPointer, false);

        var lastCall = instructions.LastOrDefault(i => i.Mnemonic == Arm64Mnemonic.BL);

        if (lastCall.Mnemonic == Arm64Mnemonic.INVALID)
        {
            Logger.VerboseNewline("Method does not match expected signature. Aborting.");
            return 0;
        }

        Logger.VerboseNewline($"Success. IsInst found at 0x{lastCall.BranchTarget:X}");
        return lastCall.BranchTarget;
    }

    protected override ulong FindFunctionThisIsAThunkOf(ulong thunkPtr, bool prioritiseCall = false)
    {
        var instructions = NewArm64Utils.GetArm64MethodBodyAtVirtualAddress(_appContext, thunkPtr, false);

        var target = prioritiseCall ? Arm64Mnemonic.BL : Arm64Mnemonic.B;
        var matchingCall = instructions.FirstOrDefault(i => i.Mnemonic == target);

        if (matchingCall.Mnemonic == Arm64Mnemonic.INVALID)
        {
            target = target == Arm64Mnemonic.BL ? Arm64Mnemonic.B : Arm64Mnemonic.BL;
            matchingCall = instructions.FirstOrDefault(i => i.Mnemonic == target);
        }

        return matchingCall.Mnemonic != Arm64Mnemonic.INVALID ? matchingCall.BranchTarget : 0;
    }

    protected override int GetCallerCount(ulong toWhere)
    {
        //Disassemble .text
        var disassembly = DisassembleTextSection();

        //Find all jumps to the target address
        return disassembly.Count(i => i.Mnemonic is Arm64Mnemonic.B or Arm64Mnemonic.BL && i.BranchTarget == toWhere);
    }

    protected override ulong GetWriteBarrier()
    {
        Logger.Verbose("\tLooking for Il2CppCodeGenWriteBarrier via corlib reference-field stores...");

        var votes = new Dictionary<ulong, int>();

        foreach (var (@namespace, typeName, methodName) in WriteBarrierAnchors)
        {
            var method = ReflectionCache.GetType(typeName, @namespace)?.Methods?.FirstOrDefault(m => m.Name == methodName);

            if (method == null || method.MethodPointer == 0)
                continue;

            var body = NewArm64Utils.GetArm64MethodBodyAtVirtualAddress(_appContext, method.MethodPointer, true);

            foreach (var target in FindWriteBarrierCalls(body).Distinct())
                votes[target] = (votes.TryGetValue(target, out var count) ? count : 0) + 1;
        }

        var best = 0ul;
        var bestVotes = 0;

        foreach (var vote in votes)
        {
            if (vote.Value <= bestVotes)
                continue;

            best = vote.Key;
            bestVotes = vote.Value;
        }

        if (best == 0)
        {
            Logger.VerboseNewline("Not found. Write barriers disabled?");
            return 0;
        }

        Logger.VerboseNewline($"Found at 0x{best:X} (found in {bestVotes} of {WriteBarrierAnchors.Length} checked methods)");

        return best;
    }

    private static IEnumerable<ulong> FindWriteBarrierCalls(List<Arm64Instruction> body)
    {
        const int window = 6;
        for (var i = 0; i < body.Count; i++)
        {
            if (body[i].Mnemonic != Arm64Mnemonic.BL)
                continue;

            if (IsPrecededByGuardedStore(body, i, window))
                yield return body[i].BranchTarget;
        }
    }

    private static bool IsPrecededByGuardedStore(List<Arm64Instruction> body, int callIndex, int window)
    {
        var start = Math.Max(0, callIndex - window);
        for (var i = start; i < callIndex; i++)
        {
            var add = body[i];
            if (add.Mnemonic == Arm64Mnemonic.ADD && add.Op0Reg == Arm64Register.X0 && add.Op2Kind == Arm64OperandKind.Immediate)
            {
                var baseReg = add.Op1Reg;
                var offset = add.Op2Imm;
                for (var j = start; j < callIndex; j++)
                {
                    var store = body[j];
                    if (store.Mnemonic is Arm64Mnemonic.STR or Arm64Mnemonic.STUR
                        && store.MemBase == baseReg && store.MemOffset == offset)
                        return true;
                    if (store.Mnemonic == Arm64Mnemonic.STP
                        && (store.MemBase == baseReg || baseReg == Arm64Register.X31 && store.MemBase == Arm64Register.X31))
                        return true;
                }
            }
        }
        return false;
    }
}
