namespace Cpp2IL.Core.ISIL;

// The address of a storage location, as produced by a lea of a stack slot.
// Normally this is a ref/out argument.
public class AddressOf(IOperand target) : IOperand
{
    public IOperand Target = target;

    public override string ToString() => $"&{Target}";
}
