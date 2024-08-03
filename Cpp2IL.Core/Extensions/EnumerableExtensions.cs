using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Cpp2IL.Core.Extensions;

internal static class EnumerableExtensions
{
    public static IEnumerable<T> MaybeAppend<T>(this IEnumerable<T> enumerable, T? item) where T : struct
    {
        if (item is not null)
        {
            return enumerable.Append(item.Value);
        }

        return enumerable;
    }

    public static MemoryEnumerable<T> AsEnumerable<T>(this Memory<T> memory) => new(memory);
    
    public static MemoryEnumerator<T> GetEnumerator<T>(this Memory<T> memory) => new(memory);

    public class MemoryEnumerable<T> : IEnumerable<T>
    {
        private readonly Memory<T> _memory;
            
        public MemoryEnumerable(Memory<T> memory)
        {
            _memory = memory;
        }
            
        public IEnumerator<T> GetEnumerator() => new MemoryEnumerator<T>(_memory);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class MemoryEnumerator<T> : IEnumerator<T>
    {
        private readonly Memory<T> _memory;
            
        private int _index = -1;
            
        public MemoryEnumerator(Memory<T> memory)
        {
            _memory = memory;
        }
            
        public bool MoveNext()
        {
            _index++;
            return _index < _memory.Length;
        }
            
        public void Reset()
        {
            _index = -1;
        }
            
        public T Current => _memory.Span[_index];

        object? IEnumerator.Current => Current;
            
        public void Dispose()
        {
            // Nothing to dispose
        }
    }
}
