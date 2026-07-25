using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Iced.Intel;

namespace Cpp2IL.Core.Il2CppApiFunctions;

public class X86KeyFunctionAddresses : BaseKeyFunctionAddresses
{
    // Corlib methods which assign a reference to a field and so are followed by a write barrier. Several,
    // because any one of them can be stripped, and because agreement between them rules out a false match.
    private static readonly (string Namespace, string Type, string Method)[] WriteBarrierAnchors =
    [
        ("System.Threading.Tasks", "Task`1", "GetAwaiter"),
        ("System.Threading.Tasks", "Task", "GetAwaiter"),
        ("System.Threading", "ExecutionContext", "get_LogicalCallContext"),
        ("System.Threading", "CancellationTokenSource", "get_Token"),
        ("System", "BadImageFormatException", "get_Message"),
    ];

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

    protected override ulong GetWriteBarrier()
    {
        Logger.Verbose("\tLooking for Il2CppCodeGenWriteBarrier via corlib reference-field stores...");

        var votes = new Dictionary<ulong, int>();

        foreach (var (@namespace, typeName, methodName) in WriteBarrierAnchors)
        {
            var method = ReflectionCache.GetType(typeName, @namespace)?.Methods?.FirstOrDefault(m => m.Name == methodName);

            if (method == null || method.MethodPointer == 0)
                continue;

            var body = X86Utils.GetMethodBodyAtVirtAddressNew(method.MethodPointer, false, _appContext.Binary);

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

    /// <summary>
    /// Returns the target of every call in <paramref name="body"/> which is preceded by both a 64-bit store
    /// into some slot and an <c>lea rcx</c> of that same slot - that is, Il2CppCodeGenWriteBarrier(&amp;slot, value)
    /// immediately following the store it guards. The two setup instructions appear in either order.
    /// </summary>
    private static IEnumerable<ulong> FindWriteBarrierCalls(InstructionList body)
    {
        const int window = 6;

        for (var i = 0; i < body.Count; i++)
        {
            var call = body[i];

            if (call.Mnemonic != Mnemonic.Call || call.Op0Kind != OpKind.NearBranch64)
                continue;

            if (IsPrecededByGuardedStore(body, i, window))
                yield return call.NearBranchTarget;
        }
    }

    private static bool IsPrecededByGuardedStore(InstructionList body, int callIndex, int window)
    {
        var start = callIndex - window < 0 ? 0 : callIndex - window;

        for (var i = start; i < callIndex; i++)
        {
            var lea = body[i];

            if (lea.Mnemonic != Mnemonic.Lea || lea.Op0Register != Register.RCX || lea.MemoryBase == Register.None)
                continue;

            for (var j = start; j < callIndex; j++)
            {
                var store = body[j];

                if (store.Mnemonic != Mnemonic.Mov || store.Op0Kind != OpKind.Memory || store.Op1Kind != OpKind.Register)
                    continue;

                if (store.MemorySize is not (MemorySize.UInt64 or MemorySize.Int64))
                    continue;

                if (store.MemoryBase == lea.MemoryBase && store.MemoryIndex == lea.MemoryIndex
                    && store.MemoryDisplacement64 == lea.MemoryDisplacement64)
                    return true;
            }
        }

        return false;
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
