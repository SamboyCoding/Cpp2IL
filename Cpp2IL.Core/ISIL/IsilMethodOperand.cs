using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.ISIL;

public readonly struct IsilMethodOperand(MethodAnalysisContext method) : IsilOperandData
{
    public readonly MethodAnalysisContext Method { get; } = method;

    public override string ToString() => Method.DeclaringType?.Name + "." + Method.Name;
}
