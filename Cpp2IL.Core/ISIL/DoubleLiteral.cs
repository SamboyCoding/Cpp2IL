using System.Globalization;

namespace Cpp2IL.Core.ISIL;

public readonly record struct DoubleLiteral(double Value) : IOperand
{
    public override string ToString() => $"{Value.ToString(CultureInfo.InvariantCulture)}d";
}
