using System.Text;

namespace Cpp2IL.Decompiler.IL;

/// <summary>
/// Memory operand in the format of [base+addend+index*scale].
/// </summary>
public struct MemoryAddress(object? baseRegister = null, object? indexRegister = null, long addend = 0, int scale = 0)
{
    /// <summary>
    /// The base.
    /// </summary>
    public object? Base = baseRegister;

    /// <summary>
    /// The index.
    /// </summary>
    public object? Index = indexRegister;

    /// <summary>
    /// Addend.
    /// </summary>
    public long Addend = addend;

    /// <summary>
    /// Scale.
    /// </summary>
    public int Scale = scale;

    /// <summary>
    /// True if this is a constant address.
    /// </summary>
    public bool IsConstant => Base == null && Index == null && Scale == 0;

    public override string ToString()
    {
        var sb = new StringBuilder("[");
        var needsPlus = false;

        if (Base != null)
        {
            sb.Append(Base);
            needsPlus = true;
        }

        if (Addend != 0)
        {
            if (needsPlus || Addend < 0)
                sb.Append(Addend > 0 ? '+' : '-');
            sb.Append($"{Math.Abs(Addend):X}");
            needsPlus = true;
        }

        if (Index != null)
        {
            if (needsPlus)
                sb.Append("+");
            sb.Append(Index);

            if (Scale > 1)
            {
                sb.Append("*");
                sb.Append(Scale);
            }
        }

        sb.Append(']');
        return sb.ToString();
    }
}
