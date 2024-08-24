using System;
using System.Globalization;

namespace Cpp2IL.Core.ISIL;

public readonly struct IsilVectorRegisterElementOperand(
    string registerName,
    IsilVectorRegisterElementOperand.VectorElementWidth width,
    int index)
    : IsilOperandData
{
    public readonly string RegisterName = registerName;
    public readonly VectorElementWidth Width = width;
    public readonly int Index = index;

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
