using System;
using System.Collections.Generic;
using System.Text;
using Iced.Intel;

namespace LibCpp2IL
{
    public static class Extensions
    {
        public static ulong GetRipBasedInstructionMemoryAddress(this Instruction instruction) => instruction.NextIP + instruction.MemoryDisplacement64;
        
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
    }
}