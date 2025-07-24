using System.Linq;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Actions;

public class PropagateTypes : IAction
{
    public int MaxLoopCount = -1;

    public void Apply(MethodAnalysisContext method)
    {
        PropagateFromReturn(method);
        PropagateFromParameters(method);
        PropagateFromCallParameters(method);
        PropagateThroughMoves(method);
    }

    private void PropagateThroughMoves(MethodAnalysisContext method)
    {
        var changed = true;
        var loopCount = 0;

        while (changed)
        {
            changed = false;
            loopCount++;

            if (MaxLoopCount != -1 && loopCount > MaxLoopCount)
                throw new DecompilerException($"Type propagation through moves not settling! (looped {MaxLoopCount} times)");

            foreach (var instruction in method.ControlFlowGraph!.Instructions)
            {
                if (instruction.OpCode != OpCode.Move && instruction.OpCode != OpCode.LoadAddress)
                    continue;

                if (instruction.Operands[0] is LocalVariable destination && instruction.Operands[1] is LocalVariable source)
                {
                    // Move ??, local
                    if (destination.Type == null && source.Type != null)
                    {
                        destination.Type = source.Type;
                        changed = true;
                    }
                    // Move local, ??
                    else if (source.Type == null && destination.Type != null)
                    {
                        source.Type = destination.Type;
                        changed = true;
                    }
                }

                if (instruction.Operands[0] is LocalVariable destination2 && instruction.Operands[1] is TypeAnalysisContext source2)
                {
                    // Move ??, type
                    if (destination2.Type == null)
                    {
                        destination2.Type = source2;
                        changed = true;
                    }
                }
            }
        }
    }

    private static void PropagateFromCallParameters(MethodAnalysisContext method)
    {
        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            if (!instruction.IsCall)
                continue;

            if (instruction.Operands[0] is not MethodAnalysisContext calledMethod)
                continue;

            // Constructor, set return variable type
            if (calledMethod.Name == ".ctor" || calledMethod.Name == ".cctor")
            {
                if (instruction.Destination is LocalVariable constructorReturn)
                {
                    constructorReturn.Type = calledMethod.DeclaringType;
                    continue;
                }
            }
            else // Return value
            {
                if (instruction.Destination is LocalVariable returnValue)
                    returnValue.Type = calledMethod.ReturnType;
            }

            // 'this' param
            if (!calledMethod.IsStatic)
            {
                if (instruction.Operands[instruction.OpCode == OpCode.CallVoid ? 1 : 2] is LocalVariable thisParam)
                    thisParam.Type = calledMethod.DeclaringType;
            }

            // Set types
            var paramOffset = calledMethod.IsStatic ? 1 : 2;
            if (instruction.OpCode == OpCode.Call) // Skip return value
                paramOffset += 1;

            for (var i = paramOffset; i < instruction.Operands.Count; i++)
            {
                var operand = instruction.Operands[i];

                if (operand is LocalVariable local)
                {
                    if ((i - paramOffset) > calledMethod.Parameters.Count - 1) // Probably MethodInfo*
                        continue;

                    local.Type = calledMethod.Parameters[i - paramOffset].ParameterType;
                }
            }
        }
    }

    private static void PropagateFromParameters(MethodAnalysisContext method)
    {
        if (method.Parameters.Count == 0)
            return;

        // 'this'
        if (!method.IsStatic)
        {
            var thisLocal = method.ParameterLocals.FirstOrDefault(p => p.IsThis);
            if (thisLocal != null)
                thisLocal.Type = method.DeclaringType;
        }

        // Normal params
        var paramIndex = 0;
        foreach (var local in method.ParameterLocals)
        {
            if (local.IsThis || local.IsMethodInfo)
                continue;

            if (paramIndex >= method.Parameters.Count)
                break;

            local.Type = method.Parameters[paramIndex].ParameterType;
            paramIndex++;
        }
    }

    private static void PropagateFromReturn(MethodAnalysisContext method)
    {
        var returns = method.ControlFlowGraph!.Instructions.Where(i => i.OpCode == OpCode.Return);

        foreach (var instruction in returns)
        {
            if (instruction.Operands.Count == 1 && instruction.Operands[0] is LocalVariable local)
                local.Type = method.ReturnType;
        }
    }
}
