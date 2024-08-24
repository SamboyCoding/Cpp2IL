using System;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL;

public class MetadataUsage(MetadataUsageType type, ulong offset, uint value)
{
    public readonly MetadataUsageType Type = type;
    public readonly ulong Offset = offset;

    private string? _cachedName;

    private Il2CppType? _cachedType;
    private Il2CppTypeReflectionData? _cachedTypeReflectionData;

    private Il2CppMethodDefinition? _cachedMethod;

    private Il2CppFieldDefinition? _cachedField;

    private string? _cachedLiteral;

    private Cpp2IlMethodRef? _cachedGenericMethod;

    public uint RawValue => value;

    public object Value
    {
        get
        {
            switch (Type)
            {
                case MetadataUsageType.Type:
                case MetadataUsageType.TypeInfo:
                    return AsType();
                case MetadataUsageType.MethodDef:
                    return AsMethod();
                case MetadataUsageType.FieldInfo:
                    return AsField();
                case MetadataUsageType.StringLiteral:
                    return AsLiteral();
                case MetadataUsageType.MethodRef:
                    return AsGenericMethodRef();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public Il2CppTypeReflectionData AsType()
    {
        if (_cachedTypeReflectionData == null)
        {
            switch (Type)
            {
                case MetadataUsageType.Type:
                case MetadataUsageType.TypeInfo:
                    try
                    {
                        _cachedType = LibCpp2IlMain.Binary!.GetType((int)value);
                        _cachedTypeReflectionData = LibCpp2ILUtils.GetTypeReflectionData(_cachedType)!;
                        _cachedName = LibCpp2ILUtils.GetTypeReflectionData(_cachedType)?.ToString();
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Failed to convert this metadata usage to a type, but it is of type {Type}, with a value of {value} (0x{value:X}). There are {LibCpp2IlMain.Binary!.NumTypes} types", e);
                    }

                    break;
                default:
                    throw new Exception($"Cannot cast metadata usage of kind {Type} to a Type");
            }
        }

        return _cachedTypeReflectionData!;
    }

    public Il2CppMethodDefinition AsMethod()
    {
        if (_cachedMethod == null)
        {
            switch (Type)
            {
                case MetadataUsageType.MethodDef:
                    _cachedMethod = LibCpp2IlMain.TheMetadata!.methodDefs[value];
                    _cachedName = _cachedMethod.GlobalKey;
                    break;
                default:
                    throw new Exception($"Cannot cast metadata usage of kind {Type} to a Method Def");
            }
        }

        return _cachedMethod!;
    }

    public Il2CppFieldDefinition AsField()
    {
        if (_cachedField == null)
        {
            switch (Type)
            {
                case MetadataUsageType.FieldInfo:
                    var fieldRef = LibCpp2IlMain.TheMetadata!.fieldRefs[value];
                    _cachedField = fieldRef.FieldDefinition;
                    _cachedName = fieldRef.DeclaringTypeDefinition!.FullName + "." + _cachedField!.Name;
                    break;
                default:
                    throw new Exception($"Cannot cast metadata usage of kind {Type} to a Field");
            }
        }

        return _cachedField;
    }

    public string AsLiteral()
    {
        if (_cachedLiteral == null)
        {
            switch (Type)
            {
                case MetadataUsageType.StringLiteral:
                    _cachedName = _cachedLiteral = LibCpp2IlMain.TheMetadata!.GetStringLiteralFromIndex(value);
                    break;
                default:
                    throw new Exception($"Cannot cast metadata usage of kind {Type} to a String Literal");
            }
        }

        return _cachedLiteral;
    }

    public Cpp2IlMethodRef AsGenericMethodRef()
    {
        if (_cachedGenericMethod == null)
        {
            switch (Type)
            {
                case MetadataUsageType.MethodRef:
                    var methodSpec = LibCpp2IlMain.Binary!.GetMethodSpec((int)value);

                    _cachedGenericMethod = new Cpp2IlMethodRef(methodSpec);
                    _cachedName = _cachedGenericMethod.ToString();
                    break;
                default:
                    throw new Exception($"Cannot cast metadata usage of kind {Type} to a Generic Method Ref");
            }
        }

        return _cachedGenericMethod;
    }

    public override string ToString()
    {
        return $"Metadata Usage {{type={Type}, Value={Value}}}";
    }

    public bool IsValid
    {
        get
        {
            try
            {
                var _ = Value;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }


    public static MetadataUsage? DecodeMetadataUsage(ulong encoded, ulong address)
    {
        var encodedType = encoded & 0xE000_0000;
        var type = (MetadataUsageType)(encodedType >> 29);
        if (type <= MetadataUsageType.MethodRef && type >= MetadataUsageType.TypeInfo)
        {
            var index = (uint)(encoded & 0x1FFF_FFFF);

            if (LibCpp2IlMain.MetadataVersion >= 27)
                index >>= 1;

            if (type is MetadataUsageType.Type or MetadataUsageType.TypeInfo && index > LibCpp2IlMain.Binary!.NumTypes)
                return null;

            if (type == MetadataUsageType.MethodDef && index > LibCpp2IlMain.TheMetadata!.methodDefs.Length)
                return null;


            return new MetadataUsage(type, address, index);
        }

        return null;
    }
}
