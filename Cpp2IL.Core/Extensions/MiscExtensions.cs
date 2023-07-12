using System.Text;

namespace Cpp2IL.Core.Extensions
{
    public static class MiscExtensions
    {
        public static Stack<T> Clone<T>(this Stack<T> original)
        {
            var arr = new T[original.Count];
            original.CopyTo(arr, 0);
            Array.Reverse(arr);
            return new Stack<T>(arr);
        }

        public static List<T> Clone<T>(this List<T> original)
        {
            var arr = new T[original.Count];
            original.CopyTo(arr, 0);
            Array.Reverse(arr);
            return new List<T>(arr);
        }

        public static Dictionary<T1, T2> Clone<T1, T2>(this Dictionary<T1, T2> original) where T1 : notnull
            => new(original);

        public static T[] SubArray<T>(this T[] data, int index, int length) => data.SubArray(index..(index + length));

        public static T RemoveAndReturn<T>(this List<T> data, int index)
        {
            var result = data[index];
            data.RemoveAt(index);
            return result;
        }

        public static IEnumerable<T> Repeat<T>(this T t, int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return t;
            }
        }

        public static string Repeat(this string source, int count)
        {
            var res = new StringBuilder();
            for (var i = 0; i < count; i++)
            {
                res.Append(source);
            }

            return res.ToString();
        }

        public static T[] SubArray<T>(this T[] source, Range range)
        {
            if (!range.Start.IsFromEnd && !range.End.IsFromEnd)
                if (range.Start.Value > range.End.Value)
                    throw new Exception($"Range {range} - Start must be less than end, when both are fixed offsets");

            var (offset, len) = range.GetOffsetAndLength(source.Length);
            var dest = new T[len];

            Array.Copy(source, offset, dest, 0, len);

            return dest;
        }

        public static T? GetValueSafely<T>(this T[] arr, int i) where T : class
        {
            if (i >= arr.Length)
                return null;

            return arr[i];
        }

        public static void TrimEndWhile<T>(this List<T> instructions, Func<T, bool> predicate)
        {
            var i = instructions.Count - 1;
            for (; i >= 0; i--)
            {
                if (!predicate(instructions[i]))
                {
                    break;
                }
            }

            var toRemove = instructions.Count - 1 - i;

            if (toRemove <= 0)
                return;

            instructions.RemoveRange(i, toRemove);
        }

        public static IEnumerable<T> Peek<T>(this IEnumerable<T> enumerable, Action<T> action)
        {
            return enumerable.Select(t =>
            {
                action(t);
                return t;
            });
        }
        
        public static unsafe uint ReadUInt(this Span<byte> span, int start)
        {
            if (start >= span.Length)
                throw new ArgumentOutOfRangeException(nameof(start), $"start=[{start}], mem.Length=[{span.Length}]");
            fixed (byte* ptr = &span[start])
                return *(uint*)ptr;
        }
    }
}
