using System;
using System.Globalization;

namespace Cpp2IL.Core.ISIL;

public readonly struct IsilVectorRegisterElementOperand : IsilOperandData
{
    public readonly string RegisterName;
    public readonly VectorElementWidth Width;
    public readonly int Index;

    public IsilVectorRegisterElementOperand(string registerName, VectorElementWidth width, int index)
    {
        RegisterName = registerName;
        Width = width;
        Index = index;
    }

    public override string ToString()
    {
        return $"{RegisterName}.{Width}[{Index}]";
    }
    
    public enum VectorElementWidth
    {
        B, //Byte
        H, //Half
        S, //Single
        D, //Double
    }
}
