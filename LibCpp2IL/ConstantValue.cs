namespace LibCpp2IL;

public readonly record struct ConstantValue(object? Value)
{
    public static ConstantValue Null => new(null);

    public static implicit operator ConstantValue(sbyte value) => new(value);
    public static implicit operator ConstantValue(byte value) => new(value);
    public static implicit operator ConstantValue(short value) => new(value);
    public static implicit operator ConstantValue(ushort value) => new(value);
    public static implicit operator ConstantValue(int value) => new(value);
    public static implicit operator ConstantValue(uint value) => new(value);
    public static implicit operator ConstantValue(long value) => new(value);
    public static implicit operator ConstantValue(ulong value) => new(value);
    public static implicit operator ConstantValue(float value) => new(value);
    public static implicit operator ConstantValue(double value) => new(value);
    public static implicit operator ConstantValue(bool value) => new(value);
    public static implicit operator ConstantValue(char value) => new(value);
    public static implicit operator ConstantValue(string? value) => new(value);

    public override string ToString()
    {
        return Value is null ? "null" : Value.ToString() ?? "";
    }
}
