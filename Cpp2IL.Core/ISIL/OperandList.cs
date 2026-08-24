using System.Collections;
using System.Collections.Generic;

namespace Cpp2IL.Core.ISIL;

// read-only view over operand storage, foreach on the concrete type uses the list's struct enumerator
public readonly struct OperandList(List<IOperand> list) : IReadOnlyList<IOperand>
{
    private readonly List<IOperand> _list = list;

    public int Count => _list.Count;

    public IOperand this[int index] => _list[index];

    public bool Contains(IOperand operand) => _list.Contains(operand);

    public List<IOperand>.Enumerator GetEnumerator() => _list.GetEnumerator();

    IEnumerator<IOperand> IEnumerable<IOperand>.GetEnumerator() => _list.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();
}
