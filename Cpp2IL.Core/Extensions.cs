using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Iced.Intel;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Mono.Collections.Generic;

namespace Cpp2IL.Core
{
    public static class Extensions
    {
        public static bool IsJump(this Mnemonic mnemonic) => mnemonic == Mnemonic.Call || mnemonic >= Mnemonic.Ja && mnemonic <= Mnemonic.Js;
        public static bool IsConditionalJump(this Mnemonic mnemonic) => mnemonic.IsJump() && mnemonic != Mnemonic.Jmp && mnemonic != Mnemonic.Call;
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
        
        public static Dictionary<T1, T2> Clone<T1, T2>(this Dictionary<T1, T2> original)
        {
            return new Dictionary<T1, T2>(original);
        }
        
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
        
        public static void MethodSignatureFullName(this IMethodSignature self, StringBuilder builder)
        {
            builder.Append("(");
            if (self.HasParameters)
            {
                Collection<ParameterDefinition> parameters = self.Parameters;
                for (int index = 0; index < parameters.Count; ++index)
                {
                    ParameterDefinition parameterDefinition = parameters[index];
                    if (index > 0)
                        builder.Append(",");
                    if (parameterDefinition.ParameterType.IsSentinel)
                        builder.Append("...,");
                    builder.Append(parameterDefinition.ParameterType.FullName);
                }
            }
            builder.Append(")");
        }

        [return: NotNullIfNotNull("unmanaged")]
        public static MethodDefinition? AsManaged(this Il2CppMethodDefinition? unmanaged)
        {
            if (unmanaged == null)
                return null;

            return SharedState.UnmanagedToManagedMethods[unmanaged];
        }
        
        [return: NotNullIfNotNull("managed")]
        public static Il2CppMethodDefinition? AsUnmanaged(this MethodDefinition? managed)
        {
            if (managed == null)
                return null;

            return SharedState.ManagedToUnmanagedMethods[managed];
        }

        public static T? GetValueSafely<T>(this Collection<T> arr, int i) where T : class
        {
            if (i >= arr.Count)
                return null;

            return arr[i];
        }

        public static bool TryPeek<T>(this Stack<T> stack, [NotNullWhen(true)] out T? result) where T : class
        {
            if (stack.Count == 0)
            {
                result = default;
                return false;
            }

            result = stack.Peek();
            return true;
        }
        
        public static bool TryPop<T>(this Stack<T> stack, [NotNullWhen(true)] out T? result) where T : class
        {
            if (stack.Count == 0)
            {
                result = default;
                return false;
            }

            result = stack.Pop();
            return true;
        }

        public static GenericParameter WithFlags(this GenericParameter genericParameter, int flags)
        {
            genericParameter.Attributes = (GenericParameterAttributes) flags;
            return genericParameter;
        }
    }
}