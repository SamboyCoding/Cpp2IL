using System;
using System.Collections.Generic;
using System.Text;

namespace LibCpp2IL;

public static class Extensions
{
    public static T[] SubArray<T>(this T[] data, int index, int length)
    {
        var result = new T[length];
        Array.Copy(data, index, result, 0, length);
        return result;
    }

    public static T RemoveAndReturn<T>(this List<T> data, int index)
    {
        var result = data[index];
        data.RemoveAt(index);
        return result;
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

    public static string ToStringEnumerable<T>(this IEnumerable<T> enumerable)
    {
        var builder = new StringBuilder("[");
        builder.Append(string.Join(", ", enumerable));
        builder.Append("]");
        return builder.ToString();
    }

    public static TValue? GetOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, TValue? defaultValue)
    {
        if (dictionary == null)
        {
            throw new ArgumentNullException(nameof(dictionary));
        }

        return dictionary.TryGetValue(key, out var value) ? value : defaultValue;
    }

    public static TValue? GetOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key) => dictionary.GetOrDefault(key, default);

    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey one, out TValue two)
    {
        one = pair.Key;
        two = pair.Value;
    }

    public static uint Bits(this uint x, int low, int count) => (x >> low) & (uint)((1 << count) - 1);

    public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value) where TKey : notnull
    {
        if (dictionary.ContainsKey(key))
            return false;

        dictionary.Add(key, value);

        return true;
    }

    /// <summary>
    /// Sorts in ascending order using the provided key function
    /// </summary>
    public static void SortByExtractedKey<T, K>(this List<T> list, Func<T, K> keyObtainer) where K : IComparable<K>
    {
        list.Sort((a, b) =>
        {
            var aKey = keyObtainer(a);
            var bKey = keyObtainer(b);

            return aKey.CompareTo(bKey);
        });
    }

    public static int ParseDigit(this char c)
    {
        var ret = c - '0';
        if (ret > 9)
            throw new($"Invalid digit {c}");

        return ret;
    }
}
