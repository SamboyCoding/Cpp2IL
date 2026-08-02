namespace Cpp2IL.Core.ISIL;

// An element of a single-dimension array (`arr[index]`)
public class ArrayAccess(LocalVariable array, IOperand index) : IOperand
{
    public LocalVariable Array = array;
    public IOperand Index = index;

    public override string ToString() => $"{Array.Name}[{Index}]";
}
