namespace LibCpp2IL;

public readonly record struct DefaultValue(object? Value)
{
    public static DefaultValue Null => new(null);

    public static implicit operator DefaultValue(sbyte value) => new(value);
    public static implicit operator DefaultValue(byte value) => new(value);
    public static implicit operator DefaultValue(short value) => new(value);
    public static implicit operator DefaultValue(ushort value) => new(value);
    public static implicit operator DefaultValue(int value) => new(value);
    public static implicit operator DefaultValue(uint value) => new(value);
    public static implicit operator DefaultValue(long value) => new(value);
    public static implicit operator DefaultValue(ulong value) => new(value);
    public static implicit operator DefaultValue(float value) => new(value);
    public static implicit operator DefaultValue(double value) => new(value);
    public static implicit operator DefaultValue(bool value) => new(value);
    public static implicit operator DefaultValue(char value) => new(value);
    public static implicit operator DefaultValue(string? value) => new(value);

    public override string ToString()
    {
        return Value is null ? "null" : Value.ToString() ?? "";
    }
}
