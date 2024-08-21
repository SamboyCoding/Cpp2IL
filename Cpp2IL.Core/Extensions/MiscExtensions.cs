using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cpp2IL.Core.ISIL;
using Gee.External.Capstone.Arm;
using Gee.External.Capstone.Arm64;
using Iced.Intel;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core.Extensions;

public static class MiscExtensions
{
    public static InstructionSetIndependentOperand MakeIndependent(this Register reg) => InstructionSetIndependentOperand.MakeRegister(reg.ToString().ToLower());

    public static ulong GetImmediateSafe(this Instruction instruction, int op) => instruction.GetOpKind(op).IsImmediate() ? instruction.GetImmediate(op) : 0;

    public static bool IsJump(this Mnemonic mnemonic) => mnemonic is Mnemonic.Call or >= Mnemonic.Ja and <= Mnemonic.Js;
    public static bool IsConditionalJump(this Mnemonic mnemonic) => mnemonic.IsJump() && mnemonic != Mnemonic.Jmp && mnemonic != Mnemonic.Call;

    //Arm Extensions
    public static ArmRegister? RegisterSafe(this ArmOperand operand) => operand.Type != ArmOperandType.Register ? null : operand.Register;
    public static bool IsImmediate(this ArmOperand operand) => operand.Type is ArmOperandType.CImmediate or ArmOperandType.Immediate or ArmOperandType.PImmediate;
    public static int ImmediateSafe(this ArmOperand operand) => operand.IsImmediate() ? operand.Immediate : 0;
    private static ArmOperand? MemoryOperand(ArmInstruction instruction) => instruction.Details.Operands.FirstOrDefault(a => a.Type == ArmOperandType.Memory);

    public static ArmRegister? MemoryBase(this ArmInstruction instruction) => MemoryOperand(instruction)?.Memory.Base;
    public static ArmRegister? MemoryIndex(this ArmInstruction instruction) => MemoryOperand(instruction)?.Memory.Index;
    public static int MemoryOffset(this ArmInstruction instruction) => MemoryOperand(instruction)?.Memory.Displacement ?? 0;

    //Arm64 Extensions
    public static Arm64Register? RegisterSafe(this Arm64Operand operand) => operand.Type != Arm64OperandType.Register ? null : operand.Register;
    public static bool IsImmediate(this Arm64Operand operand) => operand.Type is Arm64OperandType.CImmediate or Arm64OperandType.Immediate;
    public static long ImmediateSafe(this Arm64Operand operand) => operand.IsImmediate() ? operand.Immediate : 0;
    internal static Arm64Operand? MemoryOperand(this Arm64Instruction instruction) => instruction.Details.Operands.FirstOrDefault(a => a.Type == Arm64OperandType.Memory);

    public static Arm64Register? MemoryBase(this Arm64Instruction instruction) => instruction.MemoryOperand()?.Memory.Base;
    public static Arm64Register? MemoryIndex(this Arm64Instruction instruction) => instruction.MemoryOperand()?.Memory.Index;
    public static int MemoryOffset(this Arm64Instruction instruction) => instruction.MemoryOperand()?.Memory.Displacement ?? 0;

    public static bool IsConditionalMove(this Instruction instruction)
    {
            switch (instruction.Mnemonic)
            {
                case Mnemonic.Cmove:
                case Mnemonic.Cmovne:
                case Mnemonic.Cmovs:
                case Mnemonic.Cmovns:
                case Mnemonic.Cmovg:
                case Mnemonic.Cmovge:
                case Mnemonic.Cmovl:
                case Mnemonic.Cmovle:
                case Mnemonic.Cmova:
                case Mnemonic.Cmovae:
                case Mnemonic.Cmovb:
                case Mnemonic.Cmovbe:
                    return true;
                default:
                    return false;
            }
        }
    public static Stack<T> Clone<T>(this Stack<T> original)
    {
            var arr = new T[original.Count];
            original.CopyTo(arr, 0);
            Array.Reverse(arr);
            return new Stack<T>(arr);
        }

    public static List<T> Clone<T>(this List<T> original)
    {
            var arr = new T[original.Count];
            original.CopyTo(arr, 0);
            return [..arr];
        }

    public static Dictionary<T1, T2> Clone<T1, T2>(this Dictionary<T1, T2> original) where T1 : notnull
        => new(original);

    public static T[] SubArray<T>(this T[] data, int index, int length) => data.SubArray(index..(index + length));

    public static T RemoveAndReturn<T>(this List<T> data, int index)
    {
            var result = data[index];
            data.RemoveAt(index);
            return result;
        }

    public static IEnumerable<T> Repeat<T>(this T t, int count)
    {
            for (int i = 0; i < count; i++)
            {
                yield return t;
            }
        }

    public static string Repeat(this string source, int count)
    {
            var res = new StringBuilder();
            for (var i = 0; i < count; i++)
            {
                res.Append(source);
            }

            return res.ToString();
        }

    internal static T[] SubArray<T>(this T[] source, Range range)
    {
            if (!range.Start.IsFromEnd && !range.End.IsFromEnd)
                if (range.Start.Value > range.End.Value)
                    throw new Exception($"Range {range} - Start must be less than end, when both are fixed offsets");

            var (offset, len) = range.GetOffsetAndLength(source.Length);
            var dest = new T[len];

            Array.Copy(source, offset, dest, 0, len);

            return dest;
        }

    public static T? GetValueSafely<T>(this T[] arr, int i) where T : class
    {
            if (i >= arr.Length)
                return null;

            return arr[i];
        }

    public static bool IsImmediate(this OpKind opKind) => opKind is >= OpKind.Immediate8 and <= OpKind.Immediate32to64;


    public static void TrimEndWhile<T>(this List<T> instructions, Func<T, bool> predicate)
    {
            var i = instructions.Count - 1;
            for (; i >= 0; i--)
            {
                if (!predicate(instructions[i]))
                {
                    break;
                }
            }

            var toRemove = instructions.Count - 1 - i;

            if (toRemove <= 0)
                return;

            instructions.RemoveRange(i, toRemove);
        }

    public static IEnumerable<T> Peek<T>(this IEnumerable<T> enumerable, Action<T> action)
    {
            return enumerable.Select(t =>
            {
                action(t);
                return t;
            });
        }
        
    public static unsafe uint ReadUInt(this Span<byte> span, int start)
    {
            if (start >= span.Length)
                throw new ArgumentOutOfRangeException(nameof(start), $"start=[{start}], mem.Length=[{span.Length}]");
            fixed (byte* ptr = &span[start])
                return *(uint*)ptr;
        }

    public static bool BitsAreEqual(this BitArray first, BitArray second)
    {
            if (first.Count != second.Count)
                return false;
            
            bool areDifferent = false;
            for (int i = 0; i < first.Count && !areDifferent; i++)
                areDifferent =  first.Get(i) != second.Get(i);

            return !areDifferent;
        }
}