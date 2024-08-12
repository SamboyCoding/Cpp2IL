using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.ISIL;

public readonly struct IsilMethodOperand : IsilOperandData
{
    public readonly MethodAnalysisContext Method { get; }
    
    public IsilMethodOperand(MethodAnalysisContext method)
    {
        Method = method;
    }
    
    public override string ToString() => Method.DeclaringType?.Name + "." + Method.Name;
}
