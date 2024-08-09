using System;
using System.Globalization;

namespace Cpp2IL.Core.ISIL;

public readonly struct IsilImmediateOperand : IsilOperandData
{
    public readonly IConvertible Value;

    public IsilImmediateOperand(IConvertible value)
    {
        Value = value;
    }

    public override string ToString()
    {
        try
        {
            if (Convert.ToInt64(Value) > 0x1000)
                return $"0x{Value:X}";
        }
        catch
        {
            //Ignore
        }

        if (Value is string)
        {
            return "\"" + Value + "\"";
        }
        
        return Value.ToString(CultureInfo.InvariantCulture);
    }
}