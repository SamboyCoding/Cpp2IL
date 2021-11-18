using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Utils;
using Gee.External.Capstone.Arm;
using Gee.External.Capstone.Arm64;
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

        public static ulong GetInstructionAddress(this object? instruction) => instruction == null ? 0 : MiscUtils.GetAddressOfInstruction(instruction);
        
        public static ulong GetNextInstructionAddress(this object? instruction) => instruction == null ? 0 : MiscUtils.GetAddressOfNextInstruction(instruction);
        
        public static bool IsJump(this Mnemonic mnemonic) => mnemonic is Mnemonic.Call or >= Mnemonic.Ja and <= Mnemonic.Js;
        public static bool IsConditionalJump(this Mnemonic mnemonic) => mnemonic.IsJump() && mnemonic != Mnemonic.Jmp && mnemonic != Mnemonic.Call;

        //Arm Extensions
        public static ArmRegister? RegisterSafe(this ArmOperand operand) => operand.Type != ArmOperandType.Register ? null : operand.Register;
        public static bool IsImmediate(this ArmOperand operand) => operand.Type is ArmOperandType.CImmediate or ArmOperandType.Immediate or ArmOperandType.PImmediate;
        public static int ImmediateSafe(this ArmOperand operand) => operand.IsImmediate() ? operand.Immediate : 0;
        private static ArmOperand? MemoryOperand(ArmInstruction instruction) => instruction.Details.Operands.FirstOrDefault(a => a.Type == ArmOperandType.Memory);

        public static ArmRegister? MemoryBase(this ArmInstruction instruction) => MemoryOperand(instruction)?.Memory.Base;
        public static ArmRegister? MemoryIndex(this ArmInstruction instruction) => MemoryOperand(instruction)?.Memory.Index;
        public static int MemoryOffset(this ArmInstruction instruction) => MemoryOperand(instruction)?.Memory.Displacement ?? 0;
        
        //Arm64 Extensions
        public static Arm64Register? RegisterSafe(this Arm64Operand operand) => operand.Type != Arm64OperandType.Register ? null : operand.Register;
        public static bool IsImmediate(this Arm64Operand operand) => operand.Type is Arm64OperandType.CImmediate or Arm64OperandType.Immediate;
        public static long ImmediateSafe(this Arm64Operand operand) => operand.IsImmediate() ? operand.Immediate : 0;
        internal static Arm64Operand? MemoryOperand(this Arm64Instruction instruction) => instruction.Details.Operands.FirstOrDefault(a => a.Type == Arm64OperandType.Memory);

        public static Arm64Register? MemoryBase(this Arm64Instruction instruction) => instruction.MemoryOperand()?.Memory.Base;
        public static Arm64Register? MemoryIndex(this Arm64Instruction instruction) => instruction.MemoryOperand()?.Memory.Index;
        public static int MemoryOffset(this Arm64Instruction instruction) => instruction.MemoryOperand()?.Memory.Displacement ?? 0;

        public static bool IsConditionalMove(this Instruction instruction)
        {
            switch (instruction.Mnemonic)
            {
                case Mnemonic.Cmove:
                case Mnemonic.Cmovne:
                case Mnemonic.Cmovs:
                case Mnemonic.Cmovns:
                case Mnemonic.Cmovg:
                case Mnemonic.Cmovge:
                case Mnemonic.Cmovl:
                case Mnemonic.Cmovle:
                case Mnemonic.Cmova:
                case Mnemonic.Cmovae:
                case Mnemonic.Cmovb:
                case Mnemonic.Cmovbe:
                    return true;
                default:
                    return false;
            }
        }
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

        public static T[] SubArray<T>(this T[] source, Range range)
        {
            var (offset, len) = range.GetOffsetAndLength(source.Length);
            var dest = new T[len];
            
            Array.Copy(source, offset, dest, 0, len);

            return dest;
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

        [return: NotNullIfNotNull("managed")]
        public static Il2CppFieldDefinition? AsUnmanaged(this FieldDefinition? managed)
        {
            if (managed == null)
                return null;

            return SharedState.ManagedToUnmanagedFields[managed];
        }

        [return: NotNullIfNotNull("unmanaged")]
        public static FieldDefinition? AsManaged(this Il2CppFieldDefinition? unmanaged)
        {
            if (unmanaged == null)
                return null;

            return SharedState.UnmanagedToManagedFields[unmanaged];
        }
        
        [return: NotNullIfNotNull("managed")]
        public static Il2CppTypeDefinition? AsUnmanaged(this TypeDefinition? managed)
        {
            if (managed == null)
                return null;

            return SharedState.ManagedToUnmanagedTypes[managed];
        }
        
        [return: NotNullIfNotNull("unmanaged")]
        public static PropertyDefinition? AsManaged(this Il2CppPropertyDefinition? unmanaged)
        {
            if (unmanaged == null)
                return null;

            return SharedState.UnmanagedToManagedProperties[unmanaged];
        }
        
        [return: NotNullIfNotNull("managed")]
        public static Il2CppPropertyDefinition? AsUnmanaged(this PropertyDefinition? managed)
        {
            if (managed == null)
                return null;

            return SharedState.ManagedToUnmanagedProperties[managed];
        }

        [return: NotNullIfNotNull("unmanaged")]
        public static TypeDefinition? AsManaged(this Il2CppTypeDefinition? unmanaged)
        {
            if (unmanaged == null)
                return null;

            return SharedState.UnmanagedToManagedTypes[unmanaged];
        }

        public static T? GetValueSafely<T>(this Collection<T> arr, int i) where T : class
        {
            if (i >= arr.Count)
                return null;

            return arr[i];
        }
        
        public static T? GetValueSafely<T>(this T[] arr, int i) where T : class
        {
            if (i >= arr.Length)
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

        public static TypeReference ImportRecursive(this ModuleDefinition module, GenericInstanceType git, IGenericParameterProvider? context = null)
        {
            var newGit = new GenericInstanceType(module.ImportReference(git.ElementType, context));
            
            git.GenericArguments.Select(ga =>
            {
                if (ga is GenericInstanceType git2)
                    return module.ImportRecursive(git2, context);
                return module.ImportReference(ga, context);
            }).ToList().ForEach(newGit.GenericArguments.Add);

            return newGit;
        }

        public static TypeReference ImportRecursive(this ILProcessor processor, GenericInstanceType git, IGenericParameterProvider? context = null) => processor.Body.Method.Module.ImportRecursive(git, context);

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

        public static TypeReference GetUltimateElementType(this TypeSpecification specification)
        {
            if (specification.ElementType is TypeSpecification t2)
                return t2.ElementType;

            return specification.ElementType;
        }

        public static MethodReference ImportParameterTypes(this ILProcessor processor, MethodReference input)
        {
            ParameterDefinition ImportParam(ParameterDefinition parameter, ILProcessor processor)
            {
                if (parameter.ParameterType is GenericInstanceType git)
                    return new(processor.ImportReference(git.Resolve()));
                
                if (parameter.ParameterType is not GenericParameter and not ByReferenceType {ElementType: GenericParameter})
                    return new(processor.ImportReference(parameter.ParameterType));
                
                return parameter;
            }

            TypeReference ImportType(TypeReference type, ILProcessor processor)
            {
                if (type is GenericInstanceType git)
                    return processor.ImportReference(git.Resolve());
                
                if (type is not GenericParameter && !(type is TypeSpecification spec && spec.GetUltimateElementType() is GenericParameter))
                    return processor.ImportReference(type);
                
                return type;
            }

            if (input is GenericInstanceMethod gim)
            {
                //Preserve generic method arguments
                //We don't have to worry about overwriting parameters because this is a GIM, so it was specially-constructed for this one call.
                var importedParams = gim.Parameters.Select(p => ImportParam(p, processor)).ToList();
                gim.Parameters.Clear();
                importedParams.ForEach(gim.Parameters.Add);

                gim.ReturnType = ImportType(gim.ReturnType, processor);

                return gim;
            }
            
            //Copy over basic properties
            var output = new MethodReference(input.Name, input.ReturnType, input.DeclaringType)
            {
                HasThis = input.HasThis,
                ExplicitThis = input.ExplicitThis,
                CallingConvention = input.CallingConvention
            };
            
            //Copy generic params
            foreach (var generic_parameter in input.GenericParameters)
                output.GenericParameters.Add(new(generic_parameter.Name, output));
            
            //Copy params but import each one that needs importing.
            foreach (var parameter in input.Parameters)
            {
                output.Parameters.Add(ImportParam(parameter, processor));
            }
            
            output.ReturnType = ImportType(output.ReturnType, processor);

            return output;
        }

        public static TypeReference ImportReference(this ILProcessor processor, TypeReference reference, IGenericParameterProvider? context = null) => processor.Body.Method.DeclaringType.Module.ImportReference(reference, context);
        
        public static MethodReference ImportReference(this ILProcessor processor, MethodReference reference, IGenericParameterProvider? context = null) => processor.Body.Method.DeclaringType.Module.ImportReference(reference);
        
        public static FieldReference ImportReference(this ILProcessor processor, FieldReference reference, IGenericParameterProvider? context = null) => processor.Body.Method.DeclaringType.Module.ImportReference(reference);
        public static bool IsImmediate(this OpKind opKind) => opKind is >= OpKind.Immediate8 and <= OpKind.Immediate32to64;
    }
}