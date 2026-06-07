using System.Collections;

namespace Cpp2IL.Plugin.Mfuscator;

internal class SortedCollection<T> : IList<T>
{
    private readonly LinkedList<T> _sortedList;
    private readonly IComparer<T> _comparer;

    public SortedCollection(IComparer<T> comparer)
    {
        if (comparer == null) throw new ArgumentNullException("comparer");

        _comparer = comparer;
        _sortedList = new LinkedList<T>();
    }

    public SortedCollection(ICollection<T> collection) : this(Comparer<T>.Default)
    {
        if (collection == null) throw new ArgumentNullException("collection");

        foreach (var item in collection)
        {
            Add(item);
        }
    }

    public SortedCollection()
        : this(Comparer<T>.Default)
    { }

    public void Add(T item)
    {
        var node = _sortedList.First;
        if (node == null || _comparer.Compare(node.Value, item) > 0)
        {
            _sortedList.AddFirst(item);
        }
        else
        {
            while (node != null && _comparer.Compare(node.Value, item) < 1)
            {
                node = node.Next;
            }

            if (node == null)
            {
                _sortedList.AddLast(item);
            }
            else
            {
                _sortedList.AddBefore(node, item);
            }
        }
    }

    public bool Remove(T item)
    {
        return _sortedList.Remove(item);
    }

    public IEnumerator<T> GetEnumerator()
    {
        return _sortedList.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _sortedList.GetEnumerator();
    }

    public void Clear()
    {
        _sortedList.Clear();
    }

    public bool Contains(T item)
    {
        return _sortedList.Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        _sortedList.CopyTo(array, arrayIndex);
    }

    public int Count => _sortedList.Count;

    public bool IsReadOnly => false;

    public int IndexOf(T item)
    {
        if(_sortedList.Count == 0)
            return -1;
        
        var i = 0;
        var node = _sortedList.First;
        while (node != null)
        {
            if (_comparer.Compare(node.Value, item) == 0)
                return i;
            if (_comparer.Compare(node.Value, item) > 0)
                return -1; // We've passed where the item would be, so it can't be in the list.
            node = node.Next;
            i++;
        }
        
        return -1;
    }
    
    public void Insert(int index, T item)
    {
        throw new NotSupportedException("Cannot insert at a specific index in a sorted collection. Use Add() to add items in sorted order.");
    }
    
    public void RemoveAt(int index)
    {
        var node = _sortedList.First;
        for (var i = 0; i < index; i++)
        {
            if (node == null)
                throw new ArgumentOutOfRangeException(nameof(index));
            node = node.Next;
        }
        if (node == null)
            throw new ArgumentOutOfRangeException(nameof(index));
        _sortedList.Remove(node);
    }

    public T this[int index]
    {
        get
        {
            var i = 0;
            var node = _sortedList.First;
            while (node != null)
            {
                if (i == index)
                    return node.Value;
                node = node.Next;
                i++;
            }
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        set => throw new NotSupportedException("Cannot set item at a specific index in a sorted collection. Use Add() to add items in sorted order.");
    }
}
