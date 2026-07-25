using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.Analysis;

//Recovers IndirectCall (call reg) for delegate invoke back to actual calls on Invoke
public static class DelegateInvokeRecovery
{
    public static void Run(MethodAnalysisContext method)
    {
        var instructions = method.ControlFlowGraph!.Blocks.SelectMany(block => block.Instructions).ToList();

        // Il2CppObject is two pointers (klass, monitor), then method_ptr, then invoke_impl.
        var invokeImplOffset = (method.AppContext.Binary.is32Bit ? 4 : 8) * 3;

        foreach (var instruction in instructions)
        {
            if (instruction.OpCode != OpCode.IndirectCall)
                continue;

            if (GetInvokeImplLoad(instruction, instructions) is not { } memory)
                continue;

            if (memory.Addend != invokeImplOffset || memory.Index != null || memory.Scale != 0)
                continue;

            if (memory.Base is not LocalVariable delegateLocal || delegateLocal.Type is not { IsDelegate: true } delegateType)
                continue;

            if (delegateType.Methods.FirstOrDefault(m => m.Name == "Invoke") is not { } invoke)
                continue;

            RewriteAsInvoke(instruction, delegateLocal, invoke);
        }
    }

    // The address being called, whether it is still a separate load or has been inlined
    private static MemoryOperand? GetInvokeImplLoad(Instruction call, List<Instruction> instructions)
    {
        if (call.Operands.Count == 0)
            return null;

        if (call.Operands[0] is MemoryOperand folded)
            return folded;

        if (call.Operands[0] is not LocalVariable target)
            return null;

        var definition = instructions.FirstOrDefault(i => ReferenceEquals(i.Destination, target));

        return definition is { OpCode: OpCode.Move, Operands: [_, MemoryOperand loaded] } ? loaded : null;
    }

    private static void RewriteAsInvoke(Instruction call, LocalVariable delegateLocal, MethodAnalysisContext invoke)
    {
        //TODO This needs to run for the actual instruction set, not hardcoded x64
        var abi = X64CallingConventionResolver.ResolveForManaged(invoke);

        // Operands are [target, returnValue, <argument registers>], and the resolved list is in the same
        // register order, so they align from index 2 onwards
        if (call.Operands.Count - 2 < abi.Length)
            return;
        
        // TODO fix this (handle float registers not being present here and so possible DCE'd) and make this not x64 hardcoded
        if (abi.Any(operand => operand is Register register && register.Name.StartsWith("xmm")))
            return;

        var operands = new List<object> { invoke };

        if (!invoke.IsVoid)
            operands.Add(call.Operands[1]);

        operands.Add(delegateLocal); // Replaces invoke_impl_this, which isn't the delegate

        for (var i = 1; i < abi.Length; i++)
            operands.Add(call.Operands[2 + i]);

        call.OpCode = invoke.IsVoid ? OpCode.CallVoid : OpCode.Call;
        call.Operands = operands;
    }
}
