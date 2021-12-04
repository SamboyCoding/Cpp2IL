using System;

namespace Cpp2IL.Core.ISIL;

public class IsilImmediateOperand : IsilOperandData
{
    public readonly IConvertible Value;

    public IsilImmediateOperand(IConvertible value)
    {
        Value = value;
    }
}