using System;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace LibCpp2IL;

public class MetadataUsage
{
    public readonly MetadataUsageType Type;
    public readonly ulong Offset;

    private readonly uint _value;
    private readonly Il2CppBinary _binary;
    private readonly Il2CppMetadata _metadata;

    private string? _cachedName;

    private Il2CppType? _cachedType;
    private Il2CppTypeReflectionData? _cachedTypeReflectionData;

    private Il2CppMethodDefinition? _cachedMethod;

    private Il2CppFieldDefinition? _cachedField;

    private string? _cachedLiteral;

    private Cpp2IlMethodRef? _cachedGenericMethod;

    public MetadataUsage(MetadataUsageType type, ulong offset, uint value, Il2CppBinary binary, Il2CppMetadata metadata)
    {
        Type = type;
        Offset = offset;
        _value = value;
        _binary = binary;
        _metadata = metadata;
    }

    public uint RawValue => _value;

    public object Value =>
        Type switch
        {
            MetadataUsageType.Type or MetadataUsageType.TypeInfo => AsType(),
            MetadataUsageType.MethodDef => AsMethod(),
            MetadataUsageType.FieldInfo => AsField(),
            MetadataUsageType.StringLiteral => AsLiteral(),
            MetadataUsageType.MethodRef => AsGenericMethodRef(),
            _ => throw new ArgumentOutOfRangeException()
        };

    public bool IsValid =>
        Type switch
        {
            MetadataUsageType.Type or MetadataUsageType.TypeInfo => _value < _binary.NumTypes,
            MetadataUsageType.MethodDef => _value < _metadata.MethodDefinitionCount,
            MetadataUsageType.FieldInfo => _value < _metadata.fieldRefs.Length,
            MetadataUsageType.StringLiteral => _value < _metadata.stringLiterals.Length,
            MetadataUsageType.MethodRef => _value < _binary.AllGenericMethodSpecs.Length,
            _ => false
        };

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
                        _cachedType = _binary.GetType(Il2CppVariableWidthIndex<Il2CppType>.MakeTemporaryForFixedWidthUsage((int) _value)); //DynWidth: value is always masked out of 32-bits, ok for temp usage
                        _cachedTypeReflectionData = LibCpp2ILUtils.GetTypeReflectionData(_cachedType);
                        _cachedName = _cachedTypeReflectionData?.ToString();
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Failed to convert this metadata usage to a type, but it is of type {Type}, with a value of {_value} (0x{_value:X}). There are {_binary.NumTypes} types", e);
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
                    _cachedMethod = _metadata.GetMethodDefinitionFromIndex(Il2CppVariableWidthIndex<Il2CppMethodDefinition>.MakeTemporaryForFixedWidthUsage((int)_value)); //DynWidth: value is always masked out of 32-bits, ok for temp usage
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
                    var fieldRef = _metadata.fieldRefs[_value];
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
                    _cachedName = _cachedLiteral = _metadata.GetStringLiteralFromIndex(_value);
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
                    var methodSpec = _binary.GetMethodSpec((int)_value);

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

    public static MetadataUsage? DecodeMetadataUsage(ulong encoded, ulong address, Il2CppBinary binary, Il2CppMetadata metadata)
    {
        var encodedType = encoded & 0xE000_0000;
        var type = (MetadataUsageType)(encodedType >> 29);
        if (type <= MetadataUsageType.MethodRef && type >= MetadataUsageType.TypeInfo)
        {
            var index = (uint)(encoded & 0x1FFF_FFFF);

            if (metadata.MetadataVersion >= 27)
                index >>= 1;

            if (type is MetadataUsageType.Type or MetadataUsageType.TypeInfo && index > binary.NumTypes)
                return null;

            if (type == MetadataUsageType.MethodDef && index > metadata.MethodDefinitionCount)
                return null;


            return new MetadataUsage(type, address, index, binary, metadata);
        }

        return null;
    }
}
