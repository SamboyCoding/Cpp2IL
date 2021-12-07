using System;
using System.Globalization;
using System.IO;
using System.Text;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Model.Contexts;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.CustomAttributes;

/// <summary>
/// Represents a custom attribute parameter which is an instance of IConvertible (that is, a numeric type, or a nullable string)
/// </summary>
public class CustomAttributePrimitiveParameter : BaseCustomAttributeParameter
{
    private readonly Il2CppTypeEnum _primitiveType;
    public IConvertible? PrimitiveValue;

    public CustomAttributePrimitiveParameter(Il2CppTypeEnum primitiveType)
    {
        _primitiveType = primitiveType;
    }

    public override void ReadFromV29Blob(BinaryReader reader, ApplicationAnalysisContext context)
    {
        switch (_primitiveType)
        {
            case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                PrimitiveValue = reader.ReadBoolean();
                break;
            case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                PrimitiveValue = reader.ReadChar();
                break;
            case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                PrimitiveValue = reader.ReadSByte();
                break;
            case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                PrimitiveValue = reader.ReadByte();
                break;
            case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                PrimitiveValue = reader.ReadInt16();
                break;
            case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                PrimitiveValue = reader.ReadUInt16();
                break;
            case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                PrimitiveValue = reader.BaseStream.ReadUnityCompressedInt();
                break;
            case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                PrimitiveValue = reader.BaseStream.ReadUnityCompressedUint();
                break;
            case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                PrimitiveValue = reader.ReadInt64();
                break;
            case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                PrimitiveValue = reader.ReadUInt64();
                break;
            case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                PrimitiveValue = reader.ReadSingle();
                break;
            case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                PrimitiveValue = reader.ReadDouble();
                break;
            case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                var strLength = reader.BaseStream.ReadUnityCompressedInt();
                PrimitiveValue = strLength > 0 ? Encoding.UTF8.GetString(reader.ReadBytes(strLength)) : null;
                break;
            default:
                throw new Exception("CustomAttributePrimitiveParameter constructed with a non-primitive type: " + _primitiveType);
        }
    }

    public override string ToString()
    {
        if(PrimitiveValue is string s)
            return $"\"{s}\"";
        
        return PrimitiveValue?.ToString(CultureInfo.InvariantCulture) ?? "null";
    }
}