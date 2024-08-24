using System;
using System.Diagnostics;
using System.Text;

namespace Cpp2IL.Core.ISIL;

//Disable this because it's an invalid warning - the values have to be initialized or it's a compiler error in a readonly struct
// ReSharper disable RedundantDefaultMemberInitializer
/// <summary>
/// Represents a memory operand in the format of [Base + Addend + Index * Scale]
/// </summary>
public readonly struct IsilMemoryOperand : IsilOperandData
{
    public readonly InstructionSetIndependentOperand? Base = null; //Must be literal
    public readonly InstructionSetIndependentOperand? Index = null;
    public readonly long Addend = 0;
    public readonly int Scale = 0;

    /// <summary>
    /// Create a new memory operand representing just a constant address
    /// </summary>
    /// <param name="addend">The constant address which will be represented as the addent</param>
    public IsilMemoryOperand(long addend)
    {
        Addend = addend;
    }

    /// <summary>
    /// Create a new memory operand representing a base address with a zero addend.
    /// </summary>
    /// <param name="base">The base. Should be an operand of type <see cref="InstructionSetIndependentOperand.OperandType.Register"/></param>
    public IsilMemoryOperand(InstructionSetIndependentOperand @base)
    {
        Debug.Assert(@base.Type == InstructionSetIndependentOperand.OperandType.Register);

        Base = @base;
    }

    /// <summary>
    /// Create a new memory operand representing a constant offset on a base.
    /// </summary>
    /// <param name="base">The base. Should be an operand of type <see cref="InstructionSetIndependentOperand.OperandType.Register"/></param>
    /// <param name="addend">The addend relative to the memory base.</param>
    public IsilMemoryOperand(InstructionSetIndependentOperand @base, long addend)
    {
        Debug.Assert(@base.Type == InstructionSetIndependentOperand.OperandType.Register);

        Base = @base;
        Addend = addend;
    }

    /// <summary>
    /// Create a new memory operand representing a base plus an index multiplied by a constant scale.
    /// </summary>
    /// <param name="base">The base. Should be an operand of type <see cref="InstructionSetIndependentOperand.OperandType.Register"/></param>
    /// <param name="index">The index. Should be an operand of type <see cref="InstructionSetIndependentOperand.OperandType.Register"/></param>
    /// <param name="scale">The scale that the index is multiplied by. Should be a positive integer.</param>
    public IsilMemoryOperand(InstructionSetIndependentOperand @base, InstructionSetIndependentOperand index, int scale)
    {
        Debug.Assert(@base.Type == InstructionSetIndependentOperand.OperandType.Register);
        Debug.Assert(index.Type == InstructionSetIndependentOperand.OperandType.Register);
        Debug.Assert(scale > 0);

        Base = @base;
        Index = index;
        Scale = scale;
    }

    /// <summary>
    /// Create a new memory operand representing a base plus an index multiplied by a constant scale, plus a constant addend.
    /// </summary>
    /// <param name="base">The base. Should be an operand of type <see cref="InstructionSetIndependentOperand.OperandType.Register"/></param>
    /// <param name="index">The index. Should be an operand of type <see cref="InstructionSetIndependentOperand.OperandType.Register"/></param>
    /// <param name="addend">A constant addend to be added to the memory address after adding the index multiplied by the scale.</param>
    /// <param name="scale">The scale that the index is multiplied by. Should be a positive integer.</param>
    public IsilMemoryOperand(InstructionSetIndependentOperand @base, InstructionSetIndependentOperand index, long addend, int scale)
    {
        Debug.Assert(@base.Type == InstructionSetIndependentOperand.OperandType.Register);
        Debug.Assert(index.Type == InstructionSetIndependentOperand.OperandType.Register);
        Debug.Assert(scale > 0);

        Base = @base;
        Index = index;
        Addend = addend;
        Scale = scale;
    }

    public override string ToString()
    {
        var ret = new StringBuilder("[");
        var needsPlus = false;

        if (Base != null)
        {
            ret.Append(Base);
            needsPlus = true;
        }

        if (Addend != 0)
        {
            if (needsPlus)
                ret.Append(Addend > 0 ? '+' : '-');

            if (Addend > 0x10000)
                ret.AppendFormat("0x{0:X}", Math.Abs(Addend));
            else
                ret.Append(Math.Abs(Addend));
            needsPlus = true;
        }

        if (Index != null)
        {
            if (needsPlus)
                ret.Append("+");
            ret.Append(Index);

            if (Scale > 1)
            {
                ret.Append("*");
                ret.Append(Scale);
            }
        }

        ret.Append(']');

        return ret.ToString();
    }
}
