using System;
using System.Globalization;

namespace Cpp2IL.Core.ISIL;

public class IsilImmediateOperand : IsilOperandData
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
        
        return Value.ToString(CultureInfo.InvariantCulture);
    }
}