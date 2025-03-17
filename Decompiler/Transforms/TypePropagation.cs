using AsmResolver.DotNet;
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

            if (MaxLoopCount != -1 && loopCount > MaxLoopCount)
                throw new LimitReachedException($"Type propagation through moves not settling! (looped {MaxLoopCount} times)");

            foreach (var instruction in method.Instructions)
            {
                if (instruction.OpCode != OpCode.Move)
                    continue;

                if (instruction.Operands[0] is not LocalVariable local1 || instruction.Operands[1] is not LocalVariable local2)
                    continue;

                // Move ??, type
                if (local1.Type == null && local2.Type != null)
                {
                    local1.Type = local2.Type;
                    changed = true;
                }
                // Move type, ??
                else if (local2.Type == null && local1.Type != null)
                {
                    local2.Type = local1.Type;
                    changed = true;
                }
            }
        }
    }

    private static void PropagateFromCallParameters(Method method)
    {
        foreach (var instruction in method.Instructions)
        {
            if (!instruction.IsCall)
                continue;

            var isVoid = instruction.OpCode is OpCode.CallVoid or OpCode.TailCallVoid;

            var calledMethod = (MethodDefinition)instruction.Operands[isVoid ? 0 : 1];
            var isStatic = calledMethod.IsStatic;

            // Constructor, set return variable type
            if (calledMethod.IsConstructor)
            {
                if (instruction.Destination is LocalVariable constructorReturn)
                {
                    constructorReturn.Type = calledMethod.DeclaringType!.ToTypeSignature();
                    continue;
                }
            }

            // Return value
            if (instruction.Destination is LocalVariable returnValue)
                returnValue.Type = calledMethod.Parameters.ReturnParameter.ParameterType;

            if (!isStatic)
            {
                // this param
                if (instruction.Operands[isVoid ? 1 : 2] is LocalVariable thisParam)
                    thisParam.Type = calledMethod.DeclaringType!.ToTypeSignature();
            }

            // Set types
            for (var i = (isStatic ? (isVoid ? 1 : 2) : (isVoid ? 2 : 3)); i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];

                if (operand is LocalVariable local)
                {
                    if (i > calledMethod.Parameters.Count - 1)
                        continue;

                    local.Type = calledMethod.Parameters[i].ParameterType;
                }
            }
        }
    }

    private static void PropagateFromParameters(Method method)
    {
        if (method.Definition.Parameters.Count == 0)
            return;

        // This param
        if (!method.Definition.IsStatic && method.ParameterLocals.Count > 0)
            method.ParameterLocals[0].Type = method.Definition.Parameters.ThisParameter!.ParameterType;

        // Normal params
        for (var i = 0; i < method.ParameterLocals.Count; i++)
        {
            var param = method.ParameterLocals[i];

            // I don't know why this even happens
            if (i >= method.Definition.Parameters.Count)
                continue;

            var type = method.Definition.Parameters[i].ParameterType;
            param.Type = type;
        }
    }

    private static void PropagateFromReturn(Method method)
    {
        var returns = method.Instructions.Where(i => i.IsReturn);

        foreach (var instruction in returns)
        {
            if (instruction.Operands.Count == 1 && instruction.Operands[0] is LocalVariable local)
                local.Type = method.Definition.Parameters.ReturnParameter.ParameterType;
        }
    }
}
