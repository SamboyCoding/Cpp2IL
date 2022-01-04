using System;
using Cpp2IL.Core.Analysis.ResultModels;
using Gee.External.Capstone.Arm;
using Gee.External.Capstone.Arm64;
using Iced.Intel;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using WasmDisassembler;

namespace Cpp2IL.Core.Utils
{
    public static class AnalysisUtils
    {
        public static ulong GetAddressOfInstruction<T>(T t)
        {
            if (t == null)
                throw new ArgumentNullException(nameof(t));

            return t switch
            {
                Instruction x86 => x86.IP,
                ArmInstruction arm => (ulong)arm.Address,
                Arm64Instruction arm64 => (ulong)arm64.Address,
                WasmInstruction wasmInstruction => wasmInstruction.Ip,
                _ => throw new($"Unsupported instruction type {t.GetType()}"),
            };
        }

        public static ulong GetAddressOfNextInstruction<T>(T t)
        {
            if (t == null)
                throw new ArgumentNullException(nameof(t));

            return t switch
            {
                Instruction x86 => x86.NextIP,
                ArmInstruction arm => (ulong)(arm.Address + 4),
                Arm64Instruction arm64 => (ulong)(arm64.Address + 4),
                WasmInstruction wasmInstruction => wasmInstruction.NextIp,
                _ => throw new($"Unsupported instruction type {t.GetType()}"),
            };
        }

        public static long[] ReadArrayInitializerForFieldDefinition(FieldDefinition fieldDefinition, AllocatedArray allocatedArray)
        {
            var fieldDef = SharedState.ManagedToUnmanagedFields[fieldDefinition];
            var (dataIndex, _) = LibCpp2IlMain.TheMetadata!.GetFieldDefaultValue(fieldDef.FieldIndex);

            var metadata = LibCpp2IlMain.TheMetadata;

            var pointer = metadata.GetDefaultValueFromIndex(dataIndex);
            var results = new long[allocatedArray.Size];

            if (pointer <= 0) return results;

            //This should at least work for simple arrays.
            var elementSize = MiscUtils.GetSizeOfObject(allocatedArray.ArrayType.ElementType);

            for (var i = 0; i < allocatedArray.Size; i++)
            {
                results[i] = Convert.ToInt64(elementSize switch
                {
                    1 => metadata.ReadClassAtRawAddr<byte>(pointer),
                    2 => metadata.ReadClassAtRawAddr<short>(pointer),
                    4 => metadata.ReadClassAtRawAddr<int>(pointer),
                    8 => metadata.ReadClassAtRawAddr<long>(pointer),
                    _ => results[i]
                });
                pointer += (int)elementSize;
            }

            return results;
        }

        public static object? CoerceValue(object value, TypeReference coerceToType)
        {
            if (coerceToType is ArrayType)
                throw new Exception($"Can't coerce {value} to an array type {coerceToType}");

            if (value is Il2CppString)
                throw new Exception("Cannot coerce an Il2CppString. Something has gone wrong here");
            
            if (value is UnknownGlobalAddr)
                throw new Exception("Cannot coerce an UnknownGlobal. Something has gone wrong here");

            if (coerceToType.Resolve() is { IsEnum: true } enumType)
                coerceToType = enumType.GetEnumUnderlyingType();

            //Definitely both primitive
            switch (coerceToType.Name)
            {
                case "Object":
                    //This one's easy.
                    return value;
                case "Boolean":
                    return Convert.ToInt32(value) != 0;
                case "SByte":
                    return Convert.ToSByte(value);
                case "Byte":
                    if (value is ulong uValue)
                        return (byte)(int)uValue;

                    return Convert.ToByte(value);
                case "Int16":
                    return Convert.ToInt16(value);
                case "UInt16":
                    return Convert.ToUInt16(value);
                case "Int32":
                    if (value is uint u)
                        return (int)u;
                    if (value is ulong ul && ul <= uint.MaxValue)
                        return BitConverter.ToInt32(BitConverter.GetBytes((uint)ul), 0);
                    return Convert.ToInt32(value);
                case "UInt32":
                    return Convert.ToUInt32(value);
                case "Int64":
                    return Convert.ToInt64(value);
                case "UInt64":
                    return Convert.ToUInt64(value);
                case "String":
                    if (value is string)
                        return value;

                    if (Convert.ToInt32(value) == 0)
                        return null;
                    break; //Fail through to failure below.
                case "Single":
                    if (Convert.ToInt32(value) == 0)
                        return 0f;
                    break; //Fail
                case "Double":
                    if (Convert.ToInt32(value) == 0)
                        return 0d;
                    break; //Fail
                case "Type":
                    if (Convert.ToInt32(value) == 0)
                        return null;
                    break; //Fail
            }

            throw new Exception($"Can't coerce {value} to {coerceToType}");
        }

        public static void CoerceUnknownGlobalValue(TypeReference targetType, UnknownGlobalAddr unknownGlobalAddr, ConstantDefinition destinationConstant, bool allowByteArray = true)
        {
            var ulongValue = BitConverter.ToUInt64(unknownGlobalAddr.FirstTenBytes.SubArray(0, 8), 0);

            var converted = MiscUtils.ReinterpretBytes(ulongValue, targetType);
            destinationConstant.Value = converted;
            destinationConstant.Type = typeof(int).Module.GetType(targetType.FullName);
        }
    }
}