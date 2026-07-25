using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

/// <summary>
/// Fixes the compare-a-flag-against-zero pairs the we get for every conditional: <c>bool != 0</c>
/// is the bool itself, and <c>bool == 0</c> is its negation.
/// </summary>
public static class BooleanFlagSimplifier
{
    public static void Run(MethodAnalysisContext method)
    {
        var booleanType = method.AppContext.SystemTypes.SystemBooleanType;

        foreach (var instruction in method.ControlFlowGraph!.Instructions)
        {
            if (instruction.OpCode is not (OpCode.CheckEqual or OpCode.CheckNotEqual) || instruction.Operands.Count < 3)
                continue;

            if (!IsZeroConstant(instruction.Operands[2]))
                continue;

            if (instruction.Operands[1] is not LocalVariable { Type: { } type } || type != booleanType)
                continue;

            instruction.OpCode = instruction.OpCode == OpCode.CheckNotEqual ? OpCode.Move : OpCode.Not;
            instruction.Operands = [instruction.Operands[0], instruction.Operands[1]];
        }
    }

    private static bool IsZeroConstant(object operand) =>
        operand switch
        {
            int i => i == 0,
            uint ui => ui == 0,
            long l => l == 0,
            ulong ul => ul == 0,
            short s => s == 0,
            ushort us => us == 0,
            byte b => b == 0,
            sbyte sb => sb == 0,
            _ => false,
        };
}
