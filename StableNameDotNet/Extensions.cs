using System;
using System.Collections.Generic;
using System.Linq;

namespace StableNameDotNet
{
    public static class Extensions
    {
        public static bool ContainsAnyInvalidSourceCodeChars(this string s, bool allowCompilerGenerated = false)
        {
            foreach (var c in s)
                switch (c)
                {
                    case >= 'a' and <= 'z':
                    case >= 'A' and <= 'Z':
                    case >= '0' and <= '9':
                    case '_' or '`':
                    case '.' or '<' or '>' when allowCompilerGenerated:
                        continue;
                    default:
                        return true;
                }

            return false;
        }

        public static TValue GetOrCreate<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, Func<TValue> defaultGetter)
        {
            if (dict.TryGetValue(key, out var value))
                return value;

            value = defaultGetter();
            dict.Add(key, value);
            return value;
        }
        
        public static string Join(this IEnumerable<string> strings, string separator = "") 
            => string.Join(separator, strings);


        public static ulong StableHash(this string str) 
            => str.Aggregate<char, ulong>(0, (current, c) => current * 37 + c);
    }
}