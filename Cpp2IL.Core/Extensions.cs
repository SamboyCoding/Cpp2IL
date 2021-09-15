using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Iced.Intel;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;
using Instruction = Iced.Intel.Instruction;

namespace Cpp2IL.Core
{
    public static class Extensions
    {
        public static ulong GetImmediateSafe(this Instruction instruction, int op) => instruction.GetOpKind(op).IsImmediate() ? instruction.GetImmediate(op) : 0;

        public static ulong GetInstructionAddress(this object? instruction) => instruction == null ? 0 : Utils.GetAddressOfInstruction(instruction);
        
        public static ulong GetNextInstructionAddress(this object? instruction) => instruction == null ? 0 : Utils.GetAddressOfNextInstruction(instruction);
        
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

        public static bool HasAnyGenericParams(this GenericInstanceType git)
        {
            if (git.GenericArguments.Any(g => g is GenericParameter))
                return true;

            if (git.GenericArguments.Any(g => g is GenericInstanceType git2 && git2.HasAnyGenericParams()))
                return true;

            return false;
        }

        public static TypeReference ImportRecursive(this ILProcessor processor, GenericInstanceType git, IGenericParameterProvider? context = null)
        {
            var newGit = new GenericInstanceType(processor.ImportReference(git.ElementType, context));
            
            git.GenericArguments.Select(ga =>
            {
                if (ga is GenericInstanceType git2)
                    return processor.ImportRecursive(git2, context);
                return processor.ImportReference(ga, context);
            }).ToList().ForEach(newGit.GenericArguments.Add);

            return newGit;
        }

        public static MethodReference ImportRecursive(this ILProcessor processor, GenericInstanceMethod gim, IGenericParameterProvider? context = null)
        {
            var newGim = new GenericInstanceMethod(processor.ImportReference(gim.ElementMethod, context));
            
            gim.GenericArguments.Select(ga =>
            {
                if (ga is GenericInstanceType git)
                    return processor.ImportRecursive(git, context);
                return processor.ImportReference(ga, context);
            }).ToList().ForEach(newGim.GenericArguments.Add);

            return newGim;
        }

        public static TypeReference ImportReference(this ILProcessor processor, TypeReference reference, IGenericParameterProvider? context = null) => processor.Body.Method.DeclaringType.Module.ImportReference(reference, context);
        
        public static MethodReference ImportReference(this ILProcessor processor, MethodReference reference, IGenericParameterProvider? context = null) => processor.Body.Method.DeclaringType.Module.ImportReference(reference);
        
        public static FieldReference ImportReference(this ILProcessor processor, FieldReference reference, IGenericParameterProvider? context = null) => processor.Body.Method.DeclaringType.Module.ImportReference(reference);
        public static bool IsImmediate(this OpKind opKind) => opKind is >= OpKind.Immediate8 and <= OpKind.Immediate32to64;
    }
}