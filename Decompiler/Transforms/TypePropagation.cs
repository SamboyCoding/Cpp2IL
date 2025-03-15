using Decompiler.IL;

namespace Decompiler.Transforms;

/// <summary>
/// Performs type propagation.
/// </summary>
public class TypePropagation : ITransform
{
    /// <summary>
    /// Max allowed loop count (-1 for no limit).
    /// </summary>
    public int MaxLoopCount = -1;

    public void Apply(Method method)
    {
        PropagateFromReturn(method);
        PropagateFromParameters(method);
        PropagateFromCallParameters(method);
        PropagateThroughMoves(method);
    }

    private void PropagateThroughMoves(Method method)
    {
        var changed = true;
        var loopCount = 0;

        while (changed)
        {
            changed = false;
            loopCount++;

            // Sometimes this gets stuck
            if (MaxLoopCount != -1 && loopCount > MaxLoopCount)
                throw new LimitReachedException("Type propagation through moves not settling!");

            foreach (var instruction in method.Instructions)
            {
                if (instruction.OpCode != OpCode.Move)
                    continue;

                if (instruction.Operands[0] is not LocalVariable local1 || instruction.Operands[1] is not LocalVariable local2)
                    continue;

                // Move ??, type
                if (local1.LocalType == null && local2.LocalType != null)
                {
                    local1.LocalType = local2.LocalType;
                    changed = true;
                }
                // Move type, ??
                else if (local2.LocalType == null && local1.LocalType != null)
                {
                    local2.LocalType = local1.LocalType;
                    changed = true;
                }
            }
        }
    }

    private static void PropagateFromCallParameters(Method method)
    {
        foreach (var instruction in method.Instructions)
        {
            if (instruction.OpCode != OpCode.Call && instruction.OpCode != OpCode.TailCall && instruction.OpCode != OpCode.Move)
                continue;

            if (instruction.OpCode != OpCode.Move || instruction.Operands[1] is not Instruction call)
                continue;

            if (call.OpCode != OpCode.Call)
                continue;

            // At this point it's call or move something, call

            var callInstruction = instruction;
            Instruction? move = null;

            // If the call is nested, take that instruction
            if (callInstruction.OpCode == OpCode.Move && callInstruction.Operands[1] is Instruction callOp)
            {
                move = callInstruction;
                callInstruction = callOp;
            }

            var calledMethod = (MethodOperand)callInstruction.Operands[0]!;

            // Constructor, set return variable type
            if (calledMethod.IsConstructor && move != null)
            {
                ((LocalVariable)move.Operands[0]!).LocalType = calledMethod.Method.DeclaringType!.ToTypeSignature();
                continue;
            }

            // Return value
            if (move != null)
                ((LocalVariable)move.Operands[0]!).LocalType = calledMethod.Method.Parameters.ReturnParameter.ParameterType;

            // Not static
            if (!calledMethod.Method.IsStatic)
            {
                // this param
                ((LocalVariable)callInstruction.Operands[1]!).LocalType = calledMethod.Method.DeclaringType!.ToTypeSignature();

                // Set types
                for (var i = 2; i < callInstruction.Operands.Count; i++)
                {
                    var operand = callInstruction.Operands[i];
                    if (operand is not LocalVariable local) continue;
                    local.LocalType = calledMethod.Method.Parameters[i - 2].ParameterType;
                }

                continue;
            }

            // Set types
            for (var i = 0; i < callInstruction.Operands.Count; i++)
            {
                var operand = callInstruction.Operands[i];
                if (operand is not LocalVariable local) continue;
                local.LocalType = calledMethod.Method.Parameters[i - 1].ParameterType;
            }
        }
    }

    private static void PropagateFromParameters(Method method)
    {
        if (method.Definition.Parameters.Count == 0)
            return;

        // this param
        if (!method.Definition.IsStatic && method.ParameterLocals.Count > 0)
            method.ParameterLocals[0].LocalType = method.Definition.Parameters.ThisParameter!.ParameterType;

        // Normal params
        for (var i = 0; i < method.ParameterLocals.Count; i++)
        {
            var param = method.ParameterLocals[i];

            // I don't know why this even happens
            if (i >= method.Definition.Parameters.Count)
                continue;

            var type = method.Definition.Parameters[i].ParameterType;
            param.LocalType = type;
        }
    }

    private static void PropagateFromReturn(Method method)
    {
        // Get return instruction
        var returnInstruction = method.Instructions.FirstOrDefault(i => i.OpCode == OpCode.Return);

        if (returnInstruction == null)
        {
            method.AddWarning("Method has no return instruction!");
            return;
        }

        // Get type from method return type
        if (returnInstruction.Operands.Count == 1 && returnInstruction.Operands[0] is LocalVariable local)
            local.LocalType = method.Definition.Parameters.ReturnParameter.ParameterType;
    }
}
