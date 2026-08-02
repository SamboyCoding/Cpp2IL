using System.Globalization;

namespace Cpp2IL.Core.ISIL;

public readonly record struct FloatLiteral(float Value) : IOperand
{
    // 'f' suffix keeps a reinterpreted float literal from reading as a plain integer
    public override string ToString() => $"{Value.ToString(CultureInfo.InvariantCulture)}f";
}
