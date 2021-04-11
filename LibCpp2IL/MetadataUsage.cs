using System;
using System.Diagnostics;
using LibCpp2IL.Metadata;
using LibCpp2IL.PE;
using LibCpp2IL.Reflection;

namespace LibCpp2IL
{
    public class MetadataUsage
    {
        public readonly MetadataUsageType Type;
        public readonly ulong Offset;
        private readonly uint _value;

        private string? _cachedName;

        private Il2CppType? _cachedType;
        private Il2CppTypeReflectionData? _cachedTypeReflectionData;

        private Il2CppMethodDefinition? _cachedMethod;

        private Il2CppFieldDefinition? _cachedField;

        private string? _cachedLiteral;

        private Il2CppGlobalGenericMethodRef? _cachedGenericMethod;

        public MetadataUsage(MetadataUsageType type, ulong offset, uint value)
        {
            Type = type;
            _value = value;
            Offset = offset;
        }

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
                        _cachedType = LibCpp2IlMain.Binary!.GetType((int) _value);
                        _cachedTypeReflectionData = LibCpp2ILUtils.GetTypeReflectionData(_cachedType)!;
                        _cachedName = LibCpp2ILUtils.GetTypeReflectionData(_cachedType)?.ToString();
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
                        _cachedMethod = LibCpp2IlMain.TheMetadata!.methodDefs[_value];
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
                        var fieldRef = LibCpp2IlMain.TheMetadata!.fieldRefs[_value];
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
                        _cachedName = _cachedLiteral = LibCpp2IlMain.TheMetadata!.GetStringLiteralFromIndex(_value);
                        break;
                    default:
                        throw new Exception($"Cannot cast metadata usage of kind {Type} to a String Literal");
                }
            }

            return _cachedLiteral;
        }

        public Il2CppGlobalGenericMethodRef AsGenericMethodRef()
        {
            if (_cachedGenericMethod == null)
            {
                switch (Type)
                {
                    case MetadataUsageType.MethodRef: 
                        var methodSpec = LibCpp2IlMain.Binary!.GetMethodSpec((int) _value);
                        
                        var typeName = methodSpec.MethodDefinition!.DeclaringType!.FullName;
                        
                        Il2CppTypeReflectionData[] declaringTypeGenericParams = new Il2CppTypeReflectionData[0];
                        if (methodSpec.classIndexIndex != -1)
                        {
                            var classInst = methodSpec.GenericClassInst;
                            declaringTypeGenericParams = LibCpp2ILUtils.GetGenericTypeParams(classInst!)!;
                            typeName += LibCpp2ILUtils.GetGenericTypeParamNames(LibCpp2IlMain.TheMetadata!, LibCpp2IlMain.Binary!, classInst!);
                        }

                        var methodName = typeName + "." + methodSpec.MethodDefinition.Name;
                        
                        Il2CppTypeReflectionData[] genericMethodParameters = new Il2CppTypeReflectionData[0];
                        if (methodSpec.methodIndexIndex != -1)
                        {
                            var methodInst = methodSpec.GenericMethodInst;
                            methodName += LibCpp2ILUtils.GetGenericTypeParamNames(LibCpp2IlMain.TheMetadata!, LibCpp2IlMain.Binary!, methodInst!);
                            genericMethodParameters = LibCpp2ILUtils.GetGenericTypeParams(methodInst!)!;
                        }

                        _cachedName = methodName + "()";

                        _cachedGenericMethod = new Il2CppGlobalGenericMethodRef
                        {
                            baseMethod = methodSpec.MethodDefinition,
                            declaringType = methodSpec.MethodDefinition.DeclaringType,
                            typeGenericParams = declaringTypeGenericParams,
                            methodGenericParams = genericMethodParameters
                        };
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

        public static MetadataUsage? DecodeMetadataUsage(ulong encoded, ulong address)
        {
            var encodedType = encoded & 0xE000_0000;
            var type = (MetadataUsageType) (encodedType >> 29);
            if (type <= MetadataUsageType.MethodRef && type >= MetadataUsageType.TypeInfo)
            {
                var index = (uint) (encoded & 0x1FFF_FFFF);

                if (LibCpp2IlMain.MetadataVersion >= 27)
                    index >>= 1;
                

                return new MetadataUsage(type, address, index);
            }

            return null;
        }
    }
}