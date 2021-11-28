using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures.Types;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Utils
{
    public static class AsmResolverUtils
    {
        private static readonly object PointerReadLock = new();
        private static readonly Dictionary<string, (TypeDefinition typeDefinition, string[] genericParams)?> CachedTypeDefsByName = new();
        
        public static ITypeDescriptor GetTypeDefFromIl2CppType(Il2CppType il2CppType)
        {
            var theDll = LibCpp2IlMain.Binary!;

            switch (il2CppType.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                    return TypeDefinitionsAsmResolver.Object.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                    return TypeDefinitionsAsmResolver.Void.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                    return TypeDefinitionsAsmResolver.Boolean.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                    return TypeDefinitionsAsmResolver.Char.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                    return TypeDefinitionsAsmResolver.SByte.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                    return TypeDefinitionsAsmResolver.Byte.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                    return TypeDefinitionsAsmResolver.Int16.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                    return TypeDefinitionsAsmResolver.UInt16.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                    return TypeDefinitionsAsmResolver.Int32.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                    return TypeDefinitionsAsmResolver.UInt32.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_I:
                    return TypeDefinitionsAsmResolver.IntPtr.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_U:
                    return TypeDefinitionsAsmResolver.UIntPtr.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                    return TypeDefinitionsAsmResolver.Int64.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                    return TypeDefinitionsAsmResolver.UInt64.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                    return TypeDefinitionsAsmResolver.Single.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                    return TypeDefinitionsAsmResolver.Double.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                    return TypeDefinitionsAsmResolver.String.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                    return TypeDefinitionsAsmResolver.TypedReference.ToTypeReference();
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                {
                    var typeDefinition = SharedState.TypeDefsByIndexNew[il2CppType.data.classIndex];
                    return typeDefinition.ToTypeReference();
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                {
                    var arrayType = theDll.ReadClassAtVirtualAddress<Il2CppArrayType>(il2CppType.data.array);
                    var oriType = theDll.GetIl2CppTypeFromPointer(arrayType.etype);
                    return ((TypeReference) GetTypeDefFromIl2CppType(oriType)).MakeArrayType(arrayType.rank);
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                {
                    var genericClass =
                        theDll.ReadClassAtVirtualAddress<Il2CppGenericClass>(il2CppType.data.generic_class);
                    TypeDefinition typeDefinition;
                    if (LibCpp2IlMain.MetadataVersion < 27f)
                    {
                        typeDefinition = SharedState.TypeDefsByIndexNew[genericClass.typeDefinitionIndex];
                    }
                    else
                    {
                        //V27 - type indexes are pointers now.
                        var type = theDll.ReadClassAtVirtualAddress<Il2CppType>((ulong)genericClass.typeDefinitionIndex);
                        type.Init();
                        typeDefinition = (TypeDefinition) GetTypeDefFromIl2CppType(type);
                    }
                    
                    var genericInstanceType = new GenericInstanceTypeSignature(typeDefinition.ToTypeReference(), typeDefinition.IsValueType);
                    var genericInst = theDll.ReadClassAtVirtualAddress<Il2CppGenericInst>(genericClass.context.class_inst);
                    ulong[] pointers;

                    lock (PointerReadLock)
                        pointers = theDll.GetPointers(genericInst.pointerStart, (long)genericInst.pointerCount);

                    var index = 0;
                    foreach (var pointer in pointers)
                    {
                        var oriType = theDll.GetIl2CppTypeFromPointer(pointer);
                        if (oriType.type is not Il2CppTypeEnum.IL2CPP_TYPE_VAR and not Il2CppTypeEnum.IL2CPP_TYPE_MVAR)
                            genericInstanceType.TypeArguments.Add(GetTypeDefFromIl2CppType(oriType).MakeTypeSignature());
                        else
                        {
                            var gpt = oriType.type is Il2CppTypeEnum.IL2CPP_TYPE_MVAR ? GenericParameterType.Method : GenericParameterType.Type;
                            var gp = LibCpp2IlMain.TheMetadata!.genericParameters[oriType.data.genericParameterIndex];
                            genericInstanceType.TypeArguments.Add(new GenericParameterSignature(gpt, gp.genericParameterIndexInOwner));
                        }
                    }

                    return genericInstanceType;
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                {
                    var oriType = theDll.GetIl2CppTypeFromPointer(il2CppType.data.type);
                    if(oriType.type is not Il2CppTypeEnum.IL2CPP_TYPE_MVAR and not Il2CppTypeEnum.IL2CPP_TYPE_VAR)
                        return GetTypeDefFromIl2CppType(oriType).MakeSzArrayType();
                    else
                    {
                        var gp = LibCpp2IlMain.TheMetadata!.genericParameters[oriType.data.genericParameterIndex];
                        var gpt = oriType.type is Il2CppTypeEnum.IL2CPP_TYPE_MVAR ? GenericParameterType.Method : GenericParameterType.Type;
                        return new GenericParameterSignature(gpt, gp.genericParameterIndexInOwner).MakeSzArrayType();
                    }
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                {
                    var gp = LibCpp2IlMain.TheMetadata!.genericParameters[il2CppType.data.genericParameterIndex];
                    var gpt = il2CppType.type is Il2CppTypeEnum.IL2CPP_TYPE_MVAR ? GenericParameterType.Method : GenericParameterType.Type;
                    return new GenericParameterSignature(gpt, gp.genericParameterIndexInOwner);
                }
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                {
                    var oriType = theDll.GetIl2CppTypeFromPointer(il2CppType.data.type);
                    return GetTypeDefFromIl2CppType(oriType).MakePointerType();
                }

                default:
                    return TypeDefinitionsAsmResolver.Object;
            }
        }

        public static GenericParameter ImportGenericParameter(Il2CppType il2CppType, bool isMethod)
        {
            switch (il2CppType.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                {
                    if (SharedState.GenericParamsByIndexNew.TryGetValue(il2CppType.data.genericParameterIndex, out var genericParameter))
                    {
                        // if (importInto is MethodDefinition mDef)
                        // {
                        //     mDef.GenericParameters.Add(genericParameter);
                        //     mDef.DeclaringType.GenericParameters[mDef.DeclaringType.GenericParameters.IndexOf(genericParameter)] = genericParameter;
                        // }

                        return genericParameter;
                    }

                    var param = LibCpp2IlMain.TheMetadata!.genericParameters[il2CppType.data.genericParameterIndex];
                    var genericName = LibCpp2IlMain.TheMetadata.GetStringFromIndex(param.nameIndex);
                    if (isMethod)
                    {
                        genericParameter = new GenericParameter(genericName, (GenericParameterAttributes) param.flags);

                        SharedState.GenericParamsByIndexNew.Add(il2CppType.data.genericParameterIndex, genericParameter);

                        param.ConstraintTypes!
                            .Select(c => new GenericParameterConstraint(GetTypeDefFromIl2CppType(c).ToTypeDefOrRef()))
                            .ToList()
                            .ForEach(genericParameter.Constraints.Add);

                        // methodDefinition.GenericParameters.Add(genericParameter);
                        // methodDefinition.DeclaringType.GenericParameters.Add(genericParameter);
                        return genericParameter;
                    }

                    // var typeDefinition = (TypeDefinition)importInto;

                    genericParameter = new GenericParameter(genericName, (GenericParameterAttributes) param.flags);

                    SharedState.GenericParamsByIndexNew.Add(il2CppType.data.genericParameterIndex, genericParameter);

                    // typeDefinition.GenericParameters.Add(genericParameter);

                    param.ConstraintTypes!
                        .Select(c => new GenericParameterConstraint(GetTypeDefFromIl2CppType(c).ToTypeDefOrRef()))
                        .ToList()
                        .ForEach(genericParameter.Constraints.Add);

                    return genericParameter;
                }
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                {
                    if (SharedState.GenericParamsByIndexNew.TryGetValue(il2CppType.data.genericParameterIndex, out var genericParameter))
                    {
                        return genericParameter;
                    }

                    // var methodDefinition = (MethodDefinition)importInto;
                    var param = LibCpp2IlMain.TheMetadata!.genericParameters[il2CppType.data.genericParameterIndex];
                    var genericName = LibCpp2IlMain.TheMetadata.GetStringFromIndex(param.nameIndex);
                    genericParameter = new GenericParameter(genericName, (GenericParameterAttributes) param.flags);

                    SharedState.GenericParamsByIndexNew.Add(il2CppType.data.genericParameterIndex, genericParameter);

                    // methodDefinition.GenericParameters.Add(genericParameter);

                    param.ConstraintTypes!
                        .Select(c => new GenericParameterConstraint(GetTypeDefFromIl2CppType(c).ToTypeDefOrRef()))
                        .ToList()
                        .ForEach(genericParameter.Constraints.Add);

                    return genericParameter;
                }
                default:
                    throw new($"Can't import {il2CppType.type} as a generic param because it isn't one");
            }
        }

        private static TypeSignature MakeTypeSignature(this ITypeDescriptor raw)
        {
            return raw switch
            {
                TypeSignature sig => sig,
                TypeDefinition def => new TypeDefOrRefSignature(def.ToTypeReference()),
                ITypeDefOrRef typeDefOrRef => new TypeDefOrRefSignature(typeDefOrRef),
                _ => throw new($"Can't make a type signature for {raw} of type {raw.GetType()}")
            };
        }
        
        public static TypeDefinition? TryLookupTypeDefKnownNotGeneric(string? name) => TryLookupTypeDefByName(name)?.typeDefinition;
        
        public static (TypeDefinition typeDefinition, string[] genericParams)? TryLookupTypeDefByName(string? name)
        {
            if (name == null) 
                return null;

            var key = name.ToLower(CultureInfo.InvariantCulture);

            if (CachedTypeDefsByName.TryGetValue(key, out var ret))
                return ret;

            var result = InternalTryLookupTypeDefByName(name);

            CachedTypeDefsByName[key] = result;

            return result;
        }
        
        private static (TypeDefinition typeDefinition, string[] genericParams)? InternalTryLookupTypeDefByName(string name)
        {
            if (TypeDefinitionsAsmResolver.GetPrimitive(name) is {} primitive)
                return new (primitive, Array.Empty<string>());
            
            //The only real cases we end up here are:
            //From explicit override resolving, because that has to be done by name
            //Sometimes in attribute restoration if we come across an object parameter, but this almost always has to be a system or cecil type, or an enum.
            //While originally initializing the TypeDefinitions class, which is always a system type
            //And during exception helper location, which is always a system type.
            //So really the only remapping we should have to handle is during explicit override restoration.

            var definedType = SharedState.AllTypeDefinitionsNew.Find(t => string.Equals(t?.FullName, name, StringComparison.OrdinalIgnoreCase));

            if (name.EndsWith("[]"))
            {
                var without = name[..^2];
                var result = TryLookupTypeDefByName(without);
                return result;
            }

            //Generics are dumb.
            var genericParams = Array.Empty<string>();
            if (definedType == null && name.Contains("<"))
            {
                //Replace < > with the number of generic params after a `
                genericParams = MiscUtils.GetGenericParams(name[(name.IndexOf("<", StringComparison.Ordinal) + 1)..^1]);
                name = name[..name.IndexOf("<", StringComparison.Ordinal)];
                if (!name.Contains("`"))
                    name = name + "`" + (genericParams.Length);

                definedType = SharedState.AllTypeDefinitionsNew.Find(t => t.FullName == name);
            }

            if (definedType != null) return (definedType, genericParams);

            //It's possible they didn't specify a `System.` prefix
            var searchString = $"System.{name}";
            definedType = SharedState.AllTypeDefinitionsNew.Find(t => string.Equals(t.FullName, searchString, StringComparison.OrdinalIgnoreCase));

            if (definedType != null) return (definedType, genericParams);

            //Still not got one? Ok, is there only one match for non FQN?
            var matches = SharedState.AllTypeDefinitionsNew.Where(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1)
                definedType = matches.First();

            if (definedType != null) 
                return (definedType, genericParams);

            if (!name.Contains("."))
                return null;

            searchString = name;
            //Try subclasses
            matches = SharedState.AllTypeDefinitionsNew.Where(t => t.FullName.Replace('/', '.').EndsWith(searchString)).ToList();
            if (matches.Count == 1)
                definedType = matches.First();

            if (definedType == null)
                return null;

            return new (definedType, genericParams);
        }
    }
}