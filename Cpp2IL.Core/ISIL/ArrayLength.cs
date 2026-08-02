namespace Cpp2IL.Core.ISIL;

public class ArrayLength(LocalVariable array) : IOperand
{
    public LocalVariable Array = array;

    public override string ToString() => $"{Array.Name}.Length";
}
