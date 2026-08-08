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
        if (invoke.AppContext.InstructionSet.CallingConventionResolver is not { } callingConventions
            || !callingConventions.HasRawArgumentLayout(call, invoke.AppContext))
            return;

        if (invoke.IsVoid)
            call.RemoveOperandAt(1);

        call.OpCode = invoke.IsVoid ? OpCode.CallVoid : OpCode.Call;
        call.SetOperand(0, invoke);

        // the receiver register holds invoke_impl_this rather than the delegate itself
        call.SetOperand(invoke.IsVoid ? 1 : 2, delegateLocal);

        callingConventions.RemapRawArguments(call, invoke);
    }
}
