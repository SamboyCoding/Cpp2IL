using System;
using System.Linq;
using Cpp2IL.Core.ISIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Analysis;

/// <summary>
/// Recovers literals which are being put into float fields/used as float constants,
/// converting them from their uint bit patterns into actual float/double literals.
/// </summary>
public static class FloatLiteralRecovery
{
    public static void Run(MethodAnalysisContext method)
    {
        foreach (var instruction in method.ControlFlowGraph!.Blocks.SelectMany(block => block.Instructions))
        {
            if (instruction.OpCode == OpCode.Move && instruction.Operands is [FieldReference field, _])
                TryConvert(instruction, 1, field.Field.FieldType);
            else if (instruction.IsCall && instruction.Operands is [MethodAnalysisContext target, ..])
                ConvertArguments(instruction, target);
        }
    }

    private static void ConvertArguments(Instruction call, MethodAnalysisContext target)
    {
        var firstArgument = (call.OpCode == OpCode.Call ? 2 : 1) + (target.IsStatic ? 0 : 1);

        for (var i = 0; i < target.Parameters.Count; i++)
        {
            var index = firstArgument + i;

            if (index >= call.Operands.Count)
                break;

            TryConvert(call, index, target.Parameters[i].ParameterType);
        }
    }

    private static void TryConvert(Instruction instruction, int operandIndex, TypeAnalysisContext type)
    {
        if (!TryGetIntegerBits(instruction.Operands[operandIndex], out var bits))
            return;

        // TODO FIXME: We have to compare by name, not reference, because a field on a generic type resolves
        // TODO FIXME: to its own Single/Double context instance rather than the canonical one in SystemTypes.
        switch (type.FullName)
        {
            case "System.Single" when !IsSubnormalSingle((uint)bits):
                instruction.Operands[operandIndex] = BitConverter.ToSingle(BitConverter.GetBytes((uint)bits), 0);
                break;
            case "System.Double" when !IsSubnormalDouble(bits):
                instruction.Operands[operandIndex] = BitConverter.ToDouble(BitConverter.GetBytes(bits), 0);
                break;
        }
    }

    private static bool TryGetIntegerBits(object operand, out ulong bits)
    {
        switch (operand)
        {
            case ulong v: bits = v; return true;
            case long v: bits = unchecked((ulong)v); return true;
            case uint v: bits = v; return true;
            case int v: bits = unchecked((uint)v); return true;
            case ushort v: bits = v; return true;
            case short v: bits = unchecked((ushort)v); return true;
            case byte v: bits = v; return true;
            case sbyte v: bits = unchecked((byte)v); return true;
            default: bits = 0; return false;
        }
    }

    // A subnormal has a zero exponent and a non-zero mantissa (zero itself is exempt). Real source
    // constants are never subnormal, so such a decode is a mislabelled integer rather than a float.
    private static bool IsSubnormalSingle(uint bits) => (bits & 0x7F800000u) == 0 && (bits & 0x007FFFFFu) != 0;

    private static bool IsSubnormalDouble(ulong bits) => (bits & 0x7FF0000000000000UL) == 0 && (bits & 0x000FFFFFFFFFFFFFUL) != 0;
}
