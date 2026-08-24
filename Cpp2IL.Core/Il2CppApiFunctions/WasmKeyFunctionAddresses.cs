using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Logging;
using LibCpp2IL.Wasm;
using WasmDisassembler;

namespace Cpp2IL.Core.Il2CppApiFunctions;

public class WasmKeyFunctionAddresses : BaseKeyFunctionAddresses
{
    protected override ulong GetObjectIsInstFromSystemType()
    {
        return 0;
    }

    // only ever called for Exception::get_Message, whose wasm signature is always iii (string ret, this, methodinfo)
    protected override ulong FindFirstCallTargetInMethod(ulong methodVa)
    {
        if (DisassembleDynCallFunction(methodVa, "iii") is not { } instructions)
            return 0;

        foreach (var instruction in instructions)
        {
            if (instruction.Mnemonic == WasmMnemonic.Call)
                return (ulong)instruction.Operands[0];
        }

        return 0;
    }

    private const int ClassInitSampleSize = 4000;

    // neither class-init is exported or thunked on wasm, and there are two, the codegen wrapper method
    // bodies mostly call, and the underlying one corlib calls.
    protected override void AttemptInstructionAnalysisToFillGaps()
    {
        if (il2cpp_codegen_runtime_class_init == 0)
        {
            Logger.Verbose("\tLooking for il2cpp_codegen_runtime_class_init by call frequency...");
            il2cpp_codegen_runtime_class_init = FindCodegenClassInit();
            Logger.VerboseNewline(il2cpp_codegen_runtime_class_init == 0 ? "Not found" : $"Found at 0x{il2cpp_codegen_runtime_class_init:X}");
        }

        if (il2cpp_runtime_class_init_actual == 0)
        {
            Logger.Verbose("\tLooking for il2cpp_runtime_class_init via corlib static getters...");
            il2cpp_runtime_class_init_actual = FindActualClassInitByAnchor("ii",
            [
                ("System.Text", "Encoding", "get_UTF8"),
                ("System.Text", "Encoding", "get_Unicode"),
                ("System.Globalization", "CultureInfo", "get_InvariantCulture")
            ]);
            Logger.VerboseNewline(il2cpp_runtime_class_init_actual == 0 ? "Not found" : $"Found at 0x{il2cpp_runtime_class_init_actual:X}");
        }
    }

    // counts the first klass-pointer call in each provided method that isn't a metadata guard or the codegen wrapper
    // as long as 2 agree, we use that
    private ulong FindActualClassInitByAnchor(string signature, List<(string Namespace, string Type, string Method)> anchors)
    {
        var file = (WasmFile)_appContext.Binary;
        var votes = new Dictionary<ulong, int>();

        foreach (var (ns, typeName, methodName) in anchors)
        {
            var type = ReflectionCache.GetType(typeName, ns);
            var method = type?.Methods?.FirstOrDefault(m => m.Name == methodName);

            if (method == null || DisassembleDynCallFunction(method.MethodPointer, signature) is not { } instructions)
                continue;

            foreach (var instruction in instructions)
            {
                if (instruction.Mnemonic != WasmMnemonic.Call)
                    continue;

                var target = (ulong)instruction.Operands[0];

                if (target == il2cpp_codegen_initialize_runtime_metadata || target == il2cpp_codegen_initialize_method || target == il2cpp_codegen_runtime_class_init)
                    continue;

                if (target >= (ulong)file.FunctionTable.Count)
                    break;

                if (file.FunctionTable[(int)target].GetType(file).ParamTypes is [WasmTypeEnum.i32])
                    votes[target] = votes.TryGetValue(target, out var count) ? count + 1 : 1;

                break; // only the first non-guard call site is interesting
            }
        }

        var best = votes.OrderByDescending(v => v.Value).FirstOrDefault();
        return best.Value >= 2 ? best.Key : 0;
    }

    private ulong FindCodegenClassInit()
    {
        var file = (WasmFile)_appContext.Binary;
        var callCounts = new Dictionary<ulong, int>();

        var sampled = 0;
        foreach (var caller in file.FunctionTable)
        {
            if (caller.IsImport || caller.AssociatedFunctionBody is not { } body)
                continue;

            if (sampled++ >= ClassInitSampleSize)
                break;

            List<WasmInstruction> instructions;
            try
            {
                instructions = Disassembler.Disassemble(body.Instructions, (uint)body.InstructionsOffset);
            }
            catch
            {
                continue;
            }

            foreach (var instruction in instructions)
                if (instruction is { Mnemonic: WasmMnemonic.Call, Operands: [ulong target] } && target < (ulong)file.FunctionTable.Count)
                    callCounts[target] = callCounts.TryGetValue(target, out var count) ? count + 1 : 1;
        }

        foreach (var candidate in callCounts.OrderByDescending(c => c.Value))
        {
            var target = candidate.Key;
            var def = file.FunctionTable[(int)target];

            if (def.IsImport || def.AssociatedFunctionBody is not { } body)
                continue;

            var type = def.GetType(file);
            if (type.ReturnCount != 0 || type.ParamTypes is not [WasmTypeEnum.i32])
                continue;

            if (IsArgumentGuardedVoidFunction(Disassembler.Disassemble(body.Instructions, (uint)body.InstructionsOffset)))
                return target;
        }

        return 0;
    }

    // the class init wrapper guards on a field of its klass argument before calling anything, whereas metadata init
    // and other frequent void helpers call with no guard, or guard on a global, so this tells them apart.
    private static bool IsArgumentGuardedVoidFunction(List<WasmInstruction> instructions)
    {
        var loadedArgumentField = false;

        for (var i = 0; i < instructions.Count; i++)
        {
            var mnemonic = instructions[i].Mnemonic;

            if (mnemonic == WasmMnemonic.Call)
                return false;

            if (mnemonic is WasmMnemonic.LocalGet && instructions[i].Operands is [0UL]
                && i + 1 < instructions.Count && instructions[i + 1].Mnemonic is >= WasmMnemonic.I32Load and <= WasmMnemonic.I64Load32_U)
                loadedArgumentField = true;

            if (loadedArgumentField && mnemonic is WasmMnemonic.BrIf or WasmMnemonic.If)
                return true;
        }

        return false;
    }

    private List<WasmInstruction>? DisassembleDynCallFunction(ulong dynCallIndex, string signature)
    {
        try
        {
            var def = ((WasmFile)_appContext.Binary).GetFunctionFromIndexAndSignature(dynCallIndex, signature);

            if (def.AssociatedFunctionBody is not { } body)
                return null;

            return Disassembler.Disassemble(body.Instructions, (uint)body.InstructionsOffset);
        }
        catch
        {
            return null;
        }
    }

    protected override IEnumerable<ulong> FindAllThunkFunctions(ulong addr, uint maxBytesBack = 0, params ulong[] addressesToIgnore)
    {
        yield break;
    }

    protected override ulong FindFunctionThisIsAThunkOf(ulong thunkPtr, bool prioritiseCall = false)
    {
        return 0;
    }

    protected override int GetCallerCount(ulong toWhere)
    {
        return 0;
    }
}
