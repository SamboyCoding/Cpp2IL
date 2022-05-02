using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace Cpp2IL.Core
{
    public static class AttributeRestorerPost29
    {
        internal static void ApplyCustomAttributesToAllTypesInAssembly(AssemblyDefinition assemblyDefinition)
        {
            var imageDef = SharedState.ManagedToUnmanagedAssemblies[assemblyDefinition];

            foreach (var typeDef in assemblyDefinition.MainModule.Types.Where(t => t.Namespace != AssemblyPopulator.InjectedNamespaceName))
                RestoreAttributesInType(imageDef, typeDef);
        }

        private static void RestoreAttributesInType(Il2CppImageDefinition imageDef, TypeDefinition typeDefinition)
        {
            var typeDef = SharedState.ManagedToUnmanagedTypes[typeDefinition];

            //Apply custom attributes to type itself
            GetCustomAttributesByAttributeIndex(imageDef, typeDefinition.Module, typeDef.token)
                .ForEach(attribute => typeDefinition.CustomAttributes.Add(attribute));

            //Apply custom attributes to fields
            foreach (var fieldDef in typeDef.Fields!)
            {
                var fieldDefinition = SharedState.UnmanagedToManagedFields[fieldDef];

                GetCustomAttributesByAttributeIndex(imageDef, typeDefinition.Module, fieldDef.token)
                    .ForEach(attribute => fieldDefinition.CustomAttributes.Add(attribute));
            }

            //Apply custom attributes to methods
            foreach (var methodDef in typeDef.Methods!)
            {
                var methodDefinition = methodDef.AsManaged();

                GetCustomAttributesByAttributeIndex(imageDef, typeDefinition.Module, methodDef.token)
                    .ForEach(attribute => methodDefinition.CustomAttributes.Add(attribute));

                var ipd = methodDef.InternalParameterData!;
                for (var i = 0; i < methodDef.parameterCount; i++)
                {
                    var paramDef = ipd[i];
                    var paramDefinition = methodDefinition.Parameters[i];

                    GetCustomAttributesByAttributeIndex(imageDef, typeDefinition.Module, paramDef.token)
                        .ForEach(attribute => paramDefinition.CustomAttributes.Add(attribute));
                }
            }

            //Apply custom attributes to properties
            foreach (var propertyDef in typeDef.Properties!)
            {
                var propertyDefinition = SharedState.UnmanagedToManagedProperties[propertyDef];

                GetCustomAttributesByAttributeIndex(imageDef, typeDefinition.Module, propertyDef.token)
                    .ForEach(attribute => propertyDefinition.CustomAttributes.Add(attribute));
            }

            //Nested Types
            foreach (var nestedType in typeDefinition.NestedTypes)
            {
                RestoreAttributesInType(imageDef, nestedType);
            }
        }

        public static List<CustomAttribute> GetCustomAttributesByAttributeIndex(Il2CppImageDefinition imageDef, ModuleDefinition moduleDefinition, uint token)
        {
            var ret = new List<CustomAttribute>();

            //Search attribute data ranges for one with matching token
            var target = new Il2CppCustomAttributeDataRange() {token = token};
            var customAttributeIndex = LibCpp2IlMain.TheMetadata!.AttributeDataRanges.BinarySearch(imageDef.customAttributeStart, (int) imageDef.customAttributeCount, target, new TokenComparer());

            if (customAttributeIndex < 0)
                return ret; //No attributes

            var attributeDataRange = LibCpp2IlMain.TheMetadata.AttributeDataRanges[customAttributeIndex];
            // var next = LibCpp2IlMain.TheMetadata.AttributeDataRanges[customAttributeIndex + 1];

            var start = LibCpp2IlMain.TheMetadata.metadataHeader.attributeDataOffset + attributeDataRange.startOffset;
            // var end = LibCpp2IlMain.TheMetadata.metadataHeader.attributeDataOffset + next.startOffset;

            //Now we start actually reading. Start is a pointer to the address in the metadata file where the attribute data is.

            //Read attribute count as compressed uint
            var attributeCount = LibCpp2IlMain.TheMetadata.ReadUnityCompressedUIntAtRawAddr(start, out var countBytes);

            //Read attribute constructors themselves
            var pos = start + countBytes;
            var constructorDefs = ReadConstructors(attributeCount, pos);
            pos += attributeCount * 4;

            foreach (var constructorDef in constructorDefs)
            {
                ret.Add(ReadAndCreateCustomAttribute(moduleDefinition, constructorDef, pos, out var bytesRead));
                pos += bytesRead;
            }

            return ret;
        }

        private static Il2CppMethodDefinition[] ReadConstructors(uint count, long pos)
        {
            //uint method definition indices
            var methodDefinitionIndices = new uint[count];
            for (var i = 0; i < count; i++)
            {
                methodDefinitionIndices[i] = LibCpp2IlMain.TheMetadata!.ReadClassAtRawAddr<uint>(pos);
                pos += 4;
            }

            return methodDefinitionIndices.Select(m => LibCpp2IlMain.TheMetadata!.methodDefs[m]).ToArray();
        }

        private static CustomAttribute ReadAndCreateCustomAttribute(ModuleDefinition module, Il2CppMethodDefinition constructor, long pos, out int bytesRead)
        {
            bytesRead = 0;
            var managedConstructor = constructor.AsManaged();
            try
            {
                var ret = new CustomAttribute(module.ImportReference(managedConstructor));

                var numCtorArgs = LibCpp2IlMain.TheMetadata!.ReadUnityCompressedUIntAtRawAddr(pos, out var compressedRead);
                bytesRead += compressedRead;
                pos += compressedRead;

                var numFields = LibCpp2IlMain.TheMetadata.ReadUnityCompressedUIntAtRawAddr(pos, out compressedRead);
                bytesRead += compressedRead;
                pos += compressedRead;

                var numProps = LibCpp2IlMain.TheMetadata.ReadUnityCompressedUIntAtRawAddr(pos, out compressedRead);
                bytesRead += compressedRead;
                pos += compressedRead;

                //Read n constructor args
                for (var i = 0; i < numCtorArgs; i++)
                {
                    var ctorParam = managedConstructor.Parameters[i];

                    var val = ReadBlob(pos, out var ctorArgBytesRead, out var typeReference);
                    bytesRead += ctorArgBytesRead;
                    pos += ctorArgBytesRead;

                    if (typeReference.IsArray && val is Array arr)
                    {
                        val = WrapArrayValuesInCustomAttributeArguments(typeReference, arr);
                    }

                    if (ctorParam.ParameterType.FullName == "System.Object")
                    {
                        //Have to wrap val in another argument
                        val = new CustomAttributeArgument(typeReference, val);
                        typeReference = TypeDefinitions.Object;
                    }

                    ret.ConstructorArguments.Add(new CustomAttributeArgument(typeReference, val));
                }

                //Read n fields
                for (var i = 0; i < numFields; i++)
                {
                    var val = ReadBlob(pos, out var fieldBytesRead, out var typeReference);
                    bytesRead += fieldBytesRead;
                    pos += fieldBytesRead;

                    FieldDefinition field;

                    var fieldIndex = LibCpp2IlMain.TheMetadata.ReadUnityCompressedIntAtRawAddr(pos, out var fieldIdxBytesRead);
                    bytesRead += fieldIdxBytesRead;
                    pos += fieldIdxBytesRead;

                    if (fieldIndex < 0)
                    {
                        var typeIndex = LibCpp2IlMain.TheMetadata.ReadUnityCompressedUIntAtRawAddr(pos, out var typeIdxBytesRead);
                        bytesRead += typeIdxBytesRead;
                        pos += typeIdxBytesRead;

                        fieldIndex = -(fieldIndex + 1);

                        var declaringType = LibCpp2IlMain.TheMetadata.typeDefs[typeIndex];
                        field = declaringType.Fields![fieldIndex].AsManaged();
                    }
                    else
                    {
                        field = constructor.DeclaringType!.Fields![fieldIndex].AsManaged();
                    }

                    if (typeReference.IsArray && val is Array arr)
                    {
                        val = WrapArrayValuesInCustomAttributeArguments(typeReference, arr);
                    }

                    ret.Fields.Add(new(field.Name, new(typeReference, val)));
                }

                //Read n props
                for (var i = 0; i < numProps; i++)
                {
                    var val = ReadBlob(pos, out var propBytesRead, out var typeReference);
                    bytesRead += propBytesRead;
                    pos += propBytesRead;

                    PropertyDefinition prop;

                    var propIndex = LibCpp2IlMain.TheMetadata.ReadUnityCompressedIntAtRawAddr(pos, out var propIdxBytesRead);
                    bytesRead += propIdxBytesRead;
                    pos += propIdxBytesRead;

                    if (propIndex < 0)
                    {
                        var typeIndex = LibCpp2IlMain.TheMetadata.ReadUnityCompressedUIntAtRawAddr(pos, out var typeIdxBytesRead);
                        bytesRead += typeIdxBytesRead;
                        pos += typeIdxBytesRead;

                        propIndex = -(propIndex + 1);

                        var declaringType = LibCpp2IlMain.TheMetadata.typeDefs[typeIndex];
                        prop = declaringType.Properties![propIndex].AsManaged();
                    }
                    else
                    {
                        prop = constructor.DeclaringType!.Properties![propIndex].AsManaged();
                    }

                    if (typeReference.IsArray && val is Array arr)
                    {
                        val = WrapArrayValuesInCustomAttributeArguments(typeReference, arr);
                    }

                    ret.Properties.Add(new(prop.Name, new(typeReference, val)));
                }

                return ret;
            }
            catch (Exception e)
            {
#if DEBUG
                throw;
#else
                Logger.WarnNewline($"Failed to parse custom attribute {constructor.DeclaringType!.FullName} due to an exception: {e.GetType()}: {e.Message}");
                return MakeFallbackAttribute(module, managedConstructor) ?? throw new("Failed to resolve AttributeAttribute type");
#endif
            }
        }

        private static object? WrapArrayValuesInCustomAttributeArguments(TypeReference typeReference, Array arr)
        {
            var arrayDefinedType = typeReference.GetElementType();
            if (arrayDefinedType.FullName == "System.Object")
                arrayDefinedType = null;

            List<CustomAttributeArgument> list = new();
            foreach (var o in arr)
            {
                if (arrayDefinedType != null)
                {
                    list.Add(new CustomAttributeArgument(arrayDefinedType, o));
                    continue;
                }

                var objectTypeName = o?.GetType()?.FullName ?? throw new NullReferenceException($"Object {o} in array {arr} of variable type and length {arr.Length} is null, so cannot determine its type");

                var typeDef = MiscUtils.TryLookupTypeDefKnownNotGeneric(objectTypeName);

                if (typeDef == null)
                    throw new Exception($"Couldn't resolve type {objectTypeName}");

                list.Add(new CustomAttributeArgument(typeDef, o));
            }

            return list.ToArray();
        }

        private static CustomAttribute? MakeFallbackAttribute(ModuleDefinition module, MethodDefinition constructor)
        {
            var attributeType = module.Types.SingleOrDefault(t => t.Namespace == AssemblyPopulator.InjectedNamespaceName && t.Name == "AttributeAttribute");

            if (attributeType == null)
                return null;

            var attributeCtor = attributeType.GetConstructors().First();

            var ca = new CustomAttribute(attributeCtor);
            var name = new CustomAttributeNamedArgument("Name", new(module.ImportReference(TypeDefinitions.String), constructor.DeclaringType.Name));
            var rva = new CustomAttributeNamedArgument("RVA", new(module.ImportReference(TypeDefinitions.String), $"0x0"));
            var offset = new CustomAttributeNamedArgument("Offset", new(module.ImportReference(TypeDefinitions.String), $"0x0"));

            ca.Fields.Add(name);
            ca.Fields.Add(rva);
            ca.Fields.Add(offset);
            return ca;
        }

        private static object? ReadBlob(long pos, out int bytesRead, out TypeReference typeReference)
        {
            bytesRead = 0;

            var type = ReadBlobType(pos, out var typeBytesRead, out typeReference);
            bytesRead += typeBytesRead;
            pos += typeBytesRead;

            var ret = ReadBlobValue(pos, type, out var dataBytesRead, out var typeOverride);
            bytesRead += dataBytesRead;

            if (typeOverride != null)
                typeReference = typeOverride;

            return ret;
        }

        private static object? ReadBlobValue(long pos, Il2CppTypeEnum type, out int bytesRead, out TypeReference? typeOverride)
        {
            bytesRead = 0;
            object? ret;
            typeOverride = null;
            var md = LibCpp2IlMain.TheMetadata!;
            switch (type)
            {
                //For most of these, we don't bother to increment pos because it's irrelevant
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                    ret = md.ReadClassAtRawAddr<bool>(pos);
                    bytesRead += 1;
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                    ret = md.ReadClassAtRawAddr<char>(pos);
                    bytesRead += 2;
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                    ret = md.ReadClassAtRawAddr<sbyte>(pos);
                    bytesRead += 1;
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                    ret = md.ReadClassAtRawAddr<byte>(pos);
                    bytesRead += 1;
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                    ret = md.ReadClassAtRawAddr<short>(pos);
                    bytesRead += 2;
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                    ret = md.ReadClassAtRawAddr<ushort>(pos);
                    bytesRead += 2;
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                    ret = md.ReadUnityCompressedIntAtRawAddr(pos, out var i4BytesRead);
                    bytesRead += i4BytesRead;
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                    ret = md.ReadUnityCompressedUIntAtRawAddr(pos, out var u4BytesRead);
                    bytesRead += u4BytesRead;
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                    ret = md.ReadClassAtRawAddr<long>(pos);
                    bytesRead += 8;
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                    ret = md.ReadClassAtRawAddr<ulong>(pos);
                    bytesRead += 8;
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                    ret = md.ReadClassAtRawAddr<float>(pos);
                    bytesRead += 4;
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                    ret = md.ReadClassAtRawAddr<double>(pos);
                    bytesRead += 8;
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                    var strLength = md.ReadUnityCompressedIntAtRawAddr(pos, out var stringLenBytesRead);
                    bytesRead += stringLenBytesRead;
                    pos += stringLenBytesRead;

                    if (strLength > 0)
                    {
                        ret = Encoding.UTF8.GetString(md.ReadByteArrayAtRawAddress(pos, strLength));
                        bytesRead += strLength;
                    }
                    else
                    {
                        ret = null;
                    }

                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    var arrLength = md.ReadUnityCompressedIntAtRawAddr(pos, out var arrayLenBytesRead);
                    bytesRead += arrayLenBytesRead;
                    pos += arrayLenBytesRead;

                    if (arrLength == -1)
                    {
                        //A length of -1 is a null array
                        ret = null;
                        break;
                    }

                    //Read the type of the array
                    var arrayElementType = ReadBlobType(pos, out var arrTypeBytesRead, out var arrTypeDef);
                    pos += arrTypeBytesRead;
                    bytesRead += arrTypeBytesRead;

                    typeOverride = arrTypeDef.MakeArrayType();

                    //Boolean for if each element is prefixed with its type
                    var arrayElementsAreDifferent = md.ReadClassAtRawAddr<bool>(pos);
                    pos++;
                    bytesRead++;

                    if (arrayElementsAreDifferent && arrayElementType != Il2CppTypeEnum.IL2CPP_TYPE_OBJECT)
                        throw new($"Array elements are different but array element type is {arrayElementType}, not IL2CPP_TYPE_OBJECT");

                    //Create a representation our side of the array
                    
                    if(arrTypeDef.Resolve().IsEnum)
                        arrTypeDef = arrTypeDef.Resolve().GetEnumUnderlyingType();
                    
                    var ourRuntimesArrayElementType = typeof(int).Module.GetType(arrTypeDef.FullName!);
                    if (ourRuntimesArrayElementType == typeof(Type))
                        ourRuntimesArrayElementType = typeof(TypeReference);
                    
                    if(ourRuntimesArrayElementType == null)
                        throw new($"Failed to find type {arrTypeDef.FullName} in corlib module");
                    
                    var resultArray = Array.CreateInstance(ourRuntimesArrayElementType, arrLength);

                    //Read the array
                    for (var i = 0; i < arrLength; i++)
                    {
                        //Read this element's type if we have to
                        var thisElementType = arrayElementType;

                        if (arrayElementsAreDifferent)
                        {
                            thisElementType = ReadBlobType(pos, out var arrElementType2BytesRead, out _);
                            bytesRead += arrElementType2BytesRead;
                            pos += arrElementType2BytesRead;
                        }

                        //Read the value
                        var arrElem = ReadBlobValue(pos, thisElementType, out var elemBytesRead, out _);
                        bytesRead += elemBytesRead;
                        pos += elemBytesRead;

                        //Set in the array
                        try
                        {
                            resultArray.SetValue(arrElem, i);
                        }
                        catch (InvalidCastException)
                        {
                            throw new Exception($"Tried to add an object of encoding type {thisElementType}, which got read as type {arrElem?.GetType()}, and a value of {arrElem}, to an array of type {resultArray.GetType()} at index {i}");
                        }
                    }

                    ret = resultArray;
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    //LibIl2Cpp is slightly unclear here. It checks that they're null, but also doesn't increment the ptr.
                    throw new("Object not implemented");
                case Il2CppTypeEnum.IL2CPP_TYPE_IL2CPP_TYPE_INDEX:
                    var typeIndex = md.ReadUnityCompressedIntAtRawAddr(pos, out var compressedBytesRead);
                    bytesRead += compressedBytesRead;
                    if (typeIndex == -1)
                        ret = null;
                    else
                    {
                        //Weirdly, libil2cpp checks "Deserialize managed object" boolean here
                        //But as far as I can see, it's actually returning typeof(x)
                        var il2CppType = LibCpp2IlMain.Binary!.GetType(typeIndex);
                        var cecilType = MiscUtils.TryResolveTypeReflectionData(LibCpp2ILUtils.GetTypeReflectionData(il2CppType));

                        if (cecilType == null)
                            throw new($"Failed to resolve type reflection data for type index {typeIndex}");

                        ret = cecilType;
                        typeOverride = TypeDefinitions.Type;
                    }

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return ret;
        }

        private static Il2CppTypeEnum ReadBlobType(long pos, out int bytesRead, out TypeReference type)
        {
            bytesRead = 0;
            var ret = (Il2CppTypeEnum) LibCpp2IlMain.TheMetadata!.ReadClassAtRawAddr<byte>(pos);
            pos += 1;
            bytesRead += 1;

            if (ret == Il2CppTypeEnum.IL2CPP_TYPE_ENUM)
            {
                var enumTypeIndex = LibCpp2IlMain.TheMetadata.ReadUnityCompressedIntAtRawAddr(pos, out var compressedBytesRead);
                bytesRead += compressedBytesRead;
                var typeDef = LibCpp2IlReflection.GetTypeDefinitionByTypeIndex(enumTypeIndex)!;
                type = typeDef.AsManaged(); //Get enum type
                ret = LibCpp2IlMain.Binary!.GetType(typeDef.elementTypeIndex).type; //Get enum underlying type's type enum
            }
            else if (ret == Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY)
            {
                type = TypeDefinitions.Array;
            }
            else
            {
                type = FromTypeEnum(ret).AsManaged();
            }

            return ret;
        }

        private static Il2CppTypeDefinition FromTypeEnum(Il2CppTypeEnum typeEnum) => typeEnum switch
        {
            Il2CppTypeEnum.IL2CPP_TYPE_VOID => TypeDefinitions.Void.AsUnmanaged(),
            Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN => TypeDefinitions.Boolean.AsUnmanaged(),
            Il2CppTypeEnum.IL2CPP_TYPE_CHAR => TypeDefinitions.Char.AsUnmanaged(),
            Il2CppTypeEnum.IL2CPP_TYPE_I1 => TypeDefinitions.SByte.AsUnmanaged(),
            Il2CppTypeEnum.IL2CPP_TYPE_U1 => TypeDefinitions.Byte.AsUnmanaged(),
            Il2CppTypeEnum.IL2CPP_TYPE_I2 => TypeDefinitions.Int16.AsUnmanaged(),
            Il2CppTypeEnum.IL2CPP_TYPE_U2 => TypeDefinitions.UInt16.AsUnmanaged(),
            Il2CppTypeEnum.IL2CPP_TYPE_I4 => TypeDefinitions.Int32.AsUnmanaged(),
            Il2CppTypeEnum.IL2CPP_TYPE_U4 => TypeDefinitions.UInt32.AsUnmanaged(),
            Il2CppTypeEnum.IL2CPP_TYPE_I8 => TypeDefinitions.Int64.AsUnmanaged(),
            Il2CppTypeEnum.IL2CPP_TYPE_U8 => TypeDefinitions.UInt64.AsUnmanaged(),
            Il2CppTypeEnum.IL2CPP_TYPE_R4 => TypeDefinitions.Single.AsUnmanaged(),
            Il2CppTypeEnum.IL2CPP_TYPE_R8 => TypeDefinitions.Double.AsUnmanaged(),
            Il2CppTypeEnum.IL2CPP_TYPE_STRING => TypeDefinitions.String.AsUnmanaged(),
            Il2CppTypeEnum.IL2CPP_TYPE_BYREF => TypeDefinitions.TypedReference.AsUnmanaged(),
            Il2CppTypeEnum.IL2CPP_TYPE_IL2CPP_TYPE_INDEX => TypeDefinitions.Type.AsUnmanaged(),
            _ => throw new ArgumentOutOfRangeException(nameof(typeEnum), typeEnum, null)
        };
    }
}