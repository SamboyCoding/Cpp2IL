using System.Collections.Generic;
using System.Linq;

namespace Cpp2IL.Core.Utils;

internal static class EnumerableExtensions
{
    public static IEnumerable<T> MaybeAppend<T>(this IEnumerable<T> enumerable, T? item) where T : struct
    {
        if (item is not null)
        {
            return enumerable.Append(item.Value);
        }
        else
        {
            return enumerable;
        }
    }
}
