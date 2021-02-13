using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Iced.Intel;
using LibCpp2IL.Metadata;
using LibCpp2IL.PE;
using LibCpp2IL.Reflection;
using Decoder = Iced.Intel.Decoder;

namespace LibCpp2IL
{
    public static class LibCpp2ILUtils
    {
        private static readonly Dictionary<int, string> TypeString = new Dictionary<int, string>
        {
            {1, "void"},
            {2, "bool"},
            {3, "char"},
            {4, "sbyte"},
            {5, "byte"},
            {6, "short"},
            {7, "ushort"},
            {8, "int"},
            {9, "uint"},
            {10, "long"},
            {11, "ulong"},
            {12, "float"},
            {13, "double"},
            {14, "string"},
            {22, "TypedReference"},
            {24, "IntPtr"},
            {25, "UIntPtr"},
            {28, "object"}
        };


        public static InstructionList DisassembleBytesNew(bool is32Bit, byte[] bytes, ulong methodBase)
        {
            var codeReader = new ByteArrayCodeReader(bytes);
            var decoder = Decoder.Create(is32Bit ? 32 : 64, codeReader);
            decoder.IP = methodBase;
            var instructions = new InstructionList();
            var endRip = decoder.IP + (uint) bytes.Length;

            while (decoder.IP < endRip)
                decoder.Decode(out instructions.AllocUninitializedElement());

            return instructions;
        }

        internal static string GetTypeName(Il2CppMetadata metadata, PE.PE cppAssembly, Il2CppTypeDefinition typeDef, bool fullName = false)
        {
            var ret = string.Empty;
            if (fullName)
            {
                ret = typeDef.Namespace;
                if (ret != string.Empty)
                {
                    ret += ".";
                }
            }

            if (typeDef.declaringTypeIndex != -1)
            {
                ret += GetTypeName(metadata, cppAssembly, cppAssembly.types[typeDef.declaringTypeIndex]) + ".";
            }

            ret += metadata.GetStringFromIndex(typeDef.nameIndex);
            var names = new List<string>();
            if (typeDef.genericContainerIndex < 0) return ret;

            var genericContainer = metadata.genericContainers[typeDef.genericContainerIndex];
            for (var i = 0; i < genericContainer.type_argc; i++)
            {
                var genericParameterIndex = genericContainer.genericParameterStart + i;
                var param = metadata.genericParameters[genericParameterIndex];
                names.Add(metadata.GetStringFromIndex(param.nameIndex));
            }

            ret = ret.Replace($"`{genericContainer.type_argc}", "");
            ret += $"<{string.Join(", ", names)}>";

            return ret;
        }

        internal static Il2CppTypeReflectionData[]? GetGenericTypeParams(Il2CppGenericInst genericInst)
        {
            if (LibCpp2IlMain.ThePe == null || LibCpp2IlMain.TheMetadata == null) return null;

            var types = new List<Il2CppTypeReflectionData>();
            var pointers = LibCpp2IlMain.ThePe.ReadClassArrayAtVirtualAddress<ulong>(genericInst.pointerStart, (long) genericInst.pointerCount);
            for (uint i = 0; i < genericInst.pointerCount; ++i)
            {
                var oriType = LibCpp2IlMain.ThePe.GetIl2CppTypeFromPointer(pointers[i]);
                types.Add(GetTypeReflectionData(oriType)!);
            }

            return types.ToArray();
        }

        internal static string GetGenericTypeParamNames(Il2CppMetadata metadata, PE.PE cppAssembly, Il2CppGenericInst genericInst)
        {
            var typeNames = new List<string>();
            var pointers = cppAssembly.ReadClassArrayAtVirtualAddress<ulong>(genericInst.pointerStart, (long) genericInst.pointerCount);
            for (uint i = 0; i < genericInst.pointerCount; ++i)
            {
                var oriType = cppAssembly.GetIl2CppTypeFromPointer(pointers[i]);
                typeNames.Add(GetTypeName(metadata, cppAssembly, oriType));
            }

            return $"<{string.Join(", ", typeNames)}>";
        }

        public static string GetTypeName(Il2CppMetadata metadata, PE.PE cppAssembly, Il2CppType type, bool fullName = false)
        {
            string ret;
            switch (type.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                {
                    var typeDef = metadata.typeDefs[type.data.classIndex];
                    ret = string.Empty;

                    ret += GetTypeName(metadata, cppAssembly, typeDef, fullName);
                    break;
                }
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                {
                    var genericClass = cppAssembly.ReadClassAtVirtualAddress<Il2CppGenericClass>(type.data.generic_class);
                    var typeDef = metadata.typeDefs[genericClass.typeDefinitionIndex];
                    ret = metadata.GetStringFromIndex(typeDef.nameIndex);
                    var genericInst = cppAssembly.ReadClassAtVirtualAddress<Il2CppGenericInst>(genericClass.context.class_inst);
                    ret = ret.Replace($"`{genericInst.pointerCount}", "");
                    ret += GetGenericTypeParamNames(metadata, cppAssembly, genericInst);
                    break;
                }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                {
                    var param = metadata.genericParameters[type.data.genericParameterIndex];
                    ret = metadata.GetStringFromIndex(param.nameIndex);
                    break;
                }
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                {
                    var arrayType = cppAssembly.ReadClassAtVirtualAddress<Il2CppArrayType>(type.data.array);
                    var oriType = cppAssembly.GetIl2CppTypeFromPointer(arrayType.etype);
                    ret = $"{GetTypeName(metadata, cppAssembly, oriType)}[{new string(',', arrayType.rank - 1)}]";
                    break;
                }
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                {
                    var oriType = cppAssembly.GetIl2CppTypeFromPointer(type.data.type);
                    ret = $"{GetTypeName(metadata, cppAssembly, oriType)}[]";
                    break;
                }
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                {
                    var oriType = cppAssembly.GetIl2CppTypeFromPointer(type.data.type);
                    ret = $"{GetTypeName(metadata, cppAssembly, oriType)}*";
                    break;
                }
                default:
                    ret = TypeString[(int) type.type];
                    break;
            }

            return ret;
        }

        internal static object? GetDefaultValue(int dataIndex, int typeIndex, Il2CppMetadata metadata, PE.PE theDll)
        {
            var pointer = metadata.GetDefaultValueFromIndex(dataIndex);
            if (pointer <= 0) return null;

            var defaultValueType = theDll.types[typeIndex];
            metadata.Position = pointer;
            switch (defaultValueType.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                    return metadata.ReadBoolean();
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                    return metadata.ReadByte();
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                    return metadata.ReadSByte();
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                    return BitConverter.ToChar(metadata.ReadBytes(2), 0);
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                    return metadata.ReadUInt16();
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                    return metadata.ReadInt16();
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                    return metadata.ReadUInt32();
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                    return metadata.ReadInt32();
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                    return metadata.ReadUInt64();
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                    return metadata.ReadInt64();
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                    return metadata.ReadSingle();
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                    return metadata.ReadDouble();
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                    var len = metadata.ReadInt32();
                    return Encoding.UTF8.GetString(metadata.ReadBytes(len));
                default:
                    return null;
            }
        }

        internal static Il2CppTypeReflectionData WrapType(Il2CppTypeDefinition what)
        {
            return new Il2CppTypeReflectionData
            {
                baseType = what,
                genericParams = new Il2CppTypeReflectionData[0],
                isGenericType = false,
                isType = true,
            };
        }

        public static Il2CppTypeReflectionData? GetTypeReflectionData(Il2CppType forWhat)
        {
            if (LibCpp2IlMain.ThePe == null || LibCpp2IlMain.TheMetadata == null) return null;

            switch (forWhat.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                    return WrapType(LibCpp2IlReflection.GetType("Object", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                    return WrapType(LibCpp2IlReflection.GetType("Void", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                    return WrapType(LibCpp2IlReflection.GetType("Boolean", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                    return WrapType(LibCpp2IlReflection.GetType("Char", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                    return WrapType(LibCpp2IlReflection.GetType("SByte", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                    return WrapType(LibCpp2IlReflection.GetType("Byte", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                    return WrapType(LibCpp2IlReflection.GetType("Int16", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                    return WrapType(LibCpp2IlReflection.GetType("UInt16", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                    return WrapType(LibCpp2IlReflection.GetType("Int32", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                    return WrapType(LibCpp2IlReflection.GetType("UInt32", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_I:
                    return WrapType(LibCpp2IlReflection.GetType("IntPtr", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_U:
                    return WrapType(LibCpp2IlReflection.GetType("UIntPtr", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                    return WrapType(LibCpp2IlReflection.GetType("Int64", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                    return WrapType(LibCpp2IlReflection.GetType("UInt64", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                    return WrapType(LibCpp2IlReflection.GetType("Single", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                    return WrapType(LibCpp2IlReflection.GetType("Double", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                    return WrapType(LibCpp2IlReflection.GetType("String", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                    return WrapType(LibCpp2IlReflection.GetType("TypedReference", "System")!);
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                    //"normal" type
                    return new Il2CppTypeReflectionData
                    {
                        baseType = LibCpp2IlMain.TheMetadata.typeDefs[forWhat.data.classIndex],
                        genericParams = new Il2CppTypeReflectionData[0],
                        isType = true,
                        isGenericType = false,
                    };
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                {
                    //Generic type
                    var genericClass = LibCpp2IlMain.ThePe.ReadClassAtVirtualAddress<Il2CppGenericClass>(forWhat.data.generic_class);
                    
                    //CHANGED IN v27: typeDefinitionIndex is a ptr to the type in the file.
                    Il2CppTypeDefinition typeDefinition;
                    if (LibCpp2IlMain.MetadataVersion < 27f)
                        typeDefinition = LibCpp2IlMain.TheMetadata.typeDefs[genericClass.typeDefinitionIndex];
                    else
                        //This is slightly annoying, because we will have already read this type, but we have to re-read it. TODO FUTURE: Make a mapping of type definition addr => type def?
                        typeDefinition = LibCpp2IlMain.ThePe.ReadClassAtVirtualAddress<Il2CppTypeDefinition>((ulong) genericClass.typeDefinitionIndex);

                    var genericInst = LibCpp2IlMain.ThePe.ReadClassAtVirtualAddress<Il2CppGenericInst>(genericClass.context.class_inst);
                    var pointers = LibCpp2IlMain.ThePe.GetPointers(genericInst.pointerStart, (long) genericInst.pointerCount);
                    var genericParams = pointers
                        .Select(pointer => LibCpp2IlMain.ThePe.GetIl2CppTypeFromPointer(pointer))
                        .Select(type => GetTypeReflectionData(type)!) //Recursive call here
                        .ToList();

                    return new Il2CppTypeReflectionData
                    {
                        baseType = typeDefinition,
                        genericParams = genericParams.ToArray(),
                        isType = true,
                        isGenericType = true,
                    };
                }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                {
                    var param = LibCpp2IlMain.TheMetadata.genericParameters[forWhat.data.genericParameterIndex];
                    var genericName = LibCpp2IlMain.TheMetadata.GetStringFromIndex(param.nameIndex);

                    return new Il2CppTypeReflectionData
                    {
                        baseType = null,
                        genericParams = new Il2CppTypeReflectionData[0],
                        isType = false,
                        isGenericType = false,
                        variableGenericParamName = genericName,
                    };
                }
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                {
                    var oriType = LibCpp2IlMain.ThePe.GetIl2CppTypeFromPointer(forWhat.data.type);
                    return new Il2CppTypeReflectionData
                    {
                        baseType = null,
                        arrayType = GetTypeReflectionData(oriType),
                        arrayRank = 1,
                        isArray = true,
                        isType = false,
                        isGenericType = false,
                        genericParams = new Il2CppTypeReflectionData[0]
                    };
                }
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                {
                    var arrayType = LibCpp2IlMain.ThePe.ReadClassAtVirtualAddress<Il2CppArrayType>(forWhat.data.array);
                    var oriType = LibCpp2IlMain.ThePe.GetIl2CppTypeFromPointer(arrayType.etype);
                    return new Il2CppTypeReflectionData
                    {
                        baseType = null,
                        arrayType = GetTypeReflectionData(oriType),
                        isArray = true,
                        isType = false,
                        arrayRank = arrayType.rank,
                        isGenericType = false,
                        genericParams = new Il2CppTypeReflectionData[0]
                    };
                }
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                {
                    var oriType = LibCpp2IlMain.ThePe.GetIl2CppTypeFromPointer(forWhat.data.type);
                    var ret = GetTypeReflectionData(oriType)!;
                    ret.isPointer = true;
                    return ret;
                }
            }

            Console.WriteLine($"Unknown type {forWhat.type}");
            return null;
        }

        public static int VersionAwareSizeOf(Type type)
        {
            var size = 0;
            foreach (var i in type.GetFields())
            {
                var attr = (VersionAttribute?) Attribute.GetCustomAttribute(i, typeof(VersionAttribute));
                if (attr != null)
                {
                    if (LibCpp2IlMain.MetadataVersion < attr.Min || LibCpp2IlMain.MetadataVersion > attr.Max)
                        continue;
                }

                switch (i.FieldType.Name)
                {
                    case "Int64":
                    case "UInt64":
                        size += 8;
                        break;
                    case "Int32":
                    case "UInt32":
                        size += 4;
                        break;
                    case "Int16":
                    case "UInt16":
                        size += 2;
                        break;
                }
            }

            return size;
        }
    }
}