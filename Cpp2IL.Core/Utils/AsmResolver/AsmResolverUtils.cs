using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Utils.AsmResolver
{
    public static class AsmResolverUtils
    {
        private static readonly Dictionary<string, (TypeDefinition typeDefinition, string[] genericParams)?> CachedTypeDefsByName = new();
        private static readonly ConcurrentDictionary<AssemblyDefinition, ReferenceImporter> ImportersByAssembly = new();

        public static readonly ConcurrentDictionary<long, TypeDefinition> TypeDefsByIndex = new();
        public static readonly ConcurrentDictionary<long, GenericParameter> GenericParamsByIndexNew = new();

        public static TypeDefinition GetPrimitiveTypeDef(Il2CppTypeEnum type) =>
            type switch
            {
                Il2CppTypeEnum.IL2CPP_TYPE_OBJECT => TypeDefinitionsAsmResolver.Object,
                Il2CppTypeEnum.IL2CPP_TYPE_VOID => TypeDefinitionsAsmResolver.Void,
                Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN => TypeDefinitionsAsmResolver.Boolean,
                Il2CppTypeEnum.IL2CPP_TYPE_CHAR => TypeDefinitionsAsmResolver.Char,
                Il2CppTypeEnum.IL2CPP_TYPE_I1 => TypeDefinitionsAsmResolver.SByte,
                Il2CppTypeEnum.IL2CPP_TYPE_U1 => TypeDefinitionsAsmResolver.Byte,
                Il2CppTypeEnum.IL2CPP_TYPE_I2 => TypeDefinitionsAsmResolver.Int16,
                Il2CppTypeEnum.IL2CPP_TYPE_U2 => TypeDefinitionsAsmResolver.UInt16,
                Il2CppTypeEnum.IL2CPP_TYPE_I4 => TypeDefinitionsAsmResolver.Int32,
                Il2CppTypeEnum.IL2CPP_TYPE_U4 => TypeDefinitionsAsmResolver.UInt32,
                Il2CppTypeEnum.IL2CPP_TYPE_I => TypeDefinitionsAsmResolver.IntPtr,
                Il2CppTypeEnum.IL2CPP_TYPE_U => TypeDefinitionsAsmResolver.UIntPtr,
                Il2CppTypeEnum.IL2CPP_TYPE_I8 => TypeDefinitionsAsmResolver.Int64,
                Il2CppTypeEnum.IL2CPP_TYPE_U8 => TypeDefinitionsAsmResolver.UInt64,
                Il2CppTypeEnum.IL2CPP_TYPE_R4 => TypeDefinitionsAsmResolver.Single,
                Il2CppTypeEnum.IL2CPP_TYPE_R8 => TypeDefinitionsAsmResolver.Double,
                Il2CppTypeEnum.IL2CPP_TYPE_STRING => TypeDefinitionsAsmResolver.String,
                Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF => TypeDefinitionsAsmResolver.TypedReference,
                Il2CppTypeEnum.IL2CPP_TYPE_IL2CPP_TYPE_INDEX => TypeDefinitionsAsmResolver.Type,
                _ => throw new ArgumentException($"Type is not a primitive - {type}", nameof(type))
            };

        public static TypeSignature GetTypeSignatureFromIl2CppType(ModuleDefinition module, Il2CppType il2CppType)
        {
            //Module is needed for generic params
            if (il2CppType == null)
                throw new ArgumentNullException(nameof(il2CppType));

            TypeSignature ret;
            switch (il2CppType.Type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                case Il2CppTypeEnum.IL2CPP_TYPE_I:
                case Il2CppTypeEnum.IL2CPP_TYPE_U:
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                    ret = GetPrimitiveTypeDef(il2CppType.Type)
                        .ToTypeSignature();
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                    ret = TypeDefsByIndex[il2CppType.Data.ClassIndex]
                        .ToTypeSignature();
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                    ret = GetTypeSignatureFromIl2CppType(module, il2CppType.GetArrayElementType())
                        .MakeArrayType(il2CppType.GetArrayRank());
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    ret = GetTypeSignatureFromIl2CppType(module, il2CppType.GetEncapsulatedType())
                        .MakeSzArrayType();
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    ret = GetTypeSignatureFromIl2CppType(module, il2CppType.GetEncapsulatedType())
                        .MakePointerType();
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR: //Generic type parameter
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR: //Generic method parameter
                    var method = il2CppType.Type == Il2CppTypeEnum.IL2CPP_TYPE_MVAR;
                    ret = new GenericParameterSignature(module, method ? GenericParameterType.Method : GenericParameterType.Type, il2CppType.GetGenericParameterDef().genericParameterIndexInOwner);
                    break;
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                {
                    var genericClass = il2CppType.GetGenericClass();
                    
                    //Get base type
                    TypeDefsByIndex.TryGetValue(genericClass.TypeDefinitionIndex, out var typeDefinition);
                    if (LibCpp2IlMain.MetadataVersion >= 27f)
                    {
                        //V27 - type indexes are pointers now.
                        var type = LibCpp2IlMain.Binary!.ReadReadableAtVirtualAddress<Il2CppType>((ulong) genericClass.TypeDefinitionIndex);
                        typeDefinition = GetTypeSignatureFromIl2CppType(module, type).Resolve() ?? throw new Exception("Unable to resolve base type for generic inst");
                    }

                    var genericInstanceType = new GenericInstanceTypeSignature(typeDefinition!, typeDefinition!.IsValueType);
                    
                    //If the generic type is declared in a type with generic params, we have to fill all its parent's params with dummy values.
                    // if (typeDefinition.DeclaringType?.GenericParameters?.Count is {} declTypeGpCount)
                    // {
                    //     for (var i = 0; i < declTypeGpCount; i++)
                    //     {
                    //         genericInstanceType.TypeArguments.Add(TypeDefinitionsAsmResolver.Object.ToTypeSignature());
                    //     }
                    // }

                    //Get generic arguments
                    var genericArgumentTypes = genericClass.Context.ClassInst.Types;

                    //Add arguments to generic instance
                    foreach (var type in genericArgumentTypes) 
                        genericInstanceType.TypeArguments.Add(GetTypeSignatureFromIl2CppType(module, type));

                    ret = genericInstanceType;
                    break;
                }
                default:
                    throw new("Don't know how to make a type signature from " + il2CppType.Type);
            }

            if (il2CppType.Byref == 1)
                ret = ret.MakeByReferenceType();

            return ret;
        }

        /// <summary>
        /// Imports the managed representation of the given il2cpp type using the given importer, and returns said type.
        /// <br/><br/>
        /// Prefer <see cref="GetTypeSignatureFromIl2CppType"/> where possible, only use this where an actual type reference is needed.
        /// Such cases would include generic parameter constraints, base types/interfaces, and event types.
        /// </summary>
        public static ITypeDefOrRef ImportReferenceFromIl2CppType(ModuleDefinition module, Il2CppType il2CppType)
        {
            if (il2CppType == null)
                throw new ArgumentNullException(nameof(il2CppType));

            switch (il2CppType.Type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                case Il2CppTypeEnum.IL2CPP_TYPE_I:
                case Il2CppTypeEnum.IL2CPP_TYPE_U:
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                    //This case, and the one below, are faster to go this way rather than delegating to type signature creation, because we can go straight from def -> ref.
                    return GetPrimitiveTypeDef(il2CppType.Type);
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                    return TypeDefsByIndex[il2CppType.Data.ClassIndex];
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    //For the rest of these, we have to make a type signature first anyway, so just delegate to signature getter
                    return GetTypeSignatureFromIl2CppType(module, il2CppType).ToTypeDefOrRef();
                default:
                    throw new("Don't know how to import a type reference from an il2cpp type of type " + il2CppType.Type);
            }
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
            if (TypeDefinitionsAsmResolver.GetPrimitive(name) is { } primitive)
                return new(primitive, Array.Empty<string>());

            //The only real cases we end up here are:
            //From explicit override resolving, because that has to be done by name
            //Sometimes in attribute restoration if we come across an object parameter, but this almost always has to be a system or cecil type, or an enum.
            //While originally initializing the TypeDefinitions class, which is always a system type
            //And during exception helper location, which is always a system type.
            //So really the only remapping we should have to handle is during explicit override restoration.

            var definedType = Cpp2IlApi.CurrentAppContext!.AllTypes.FirstOrDefault(t => t.Definition != null && string.Equals(t.Definition.FullName, name, StringComparison.OrdinalIgnoreCase));

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

                definedType = Cpp2IlApi.CurrentAppContext.AllTypes.FirstOrDefault(t => t.Definition?.FullName == name);
            }

            if (definedType != null) return (definedType.GetExtraData<TypeDefinition>("AsmResolverType")!, genericParams);

            //It's possible they didn't specify a `System.` prefix
            var searchString = $"System.{name}";
            definedType = Cpp2IlApi.CurrentAppContext.AllTypes.FirstOrDefault(t => string.Equals(t.Definition?.FullName, searchString, StringComparison.OrdinalIgnoreCase));

            if (definedType != null) return (definedType.GetExtraData<TypeDefinition>("AsmResolverType")!, genericParams);

            //Still not got one? Ok, is there only one match for non FQN?
            var matches = Cpp2IlApi.CurrentAppContext.AllTypes.Where(t => string.Equals(t.Definition?.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1)
                definedType = matches.First();

            if (definedType != null)
                return (definedType.GetExtraData<TypeDefinition>("AsmResolverType")!, genericParams);

            if (!name.Contains("."))
                return null;

            searchString = name;
            //Try subclasses
            matches = Cpp2IlApi.CurrentAppContext.AllTypes.Where(t => t.Definition != null && t.Definition.FullName!.Replace('/', '.').EndsWith(searchString)).ToList();
            if (matches.Count == 1)
                definedType = matches.First();

            if (definedType == null)
                return null;

            return new(definedType.GetExtraData<TypeDefinition>("AsmResolverType")!, genericParams);
        }

        public static ReferenceImporter GetImporter(this AssemblyDefinition assemblyDefinition)
        {
            if (ImportersByAssembly.TryGetValue(assemblyDefinition, out var ret))
                return ret;

            ImportersByAssembly[assemblyDefinition] = ret = new(assemblyDefinition.Modules[0]);

            return ret;
        }

        public static ITypeDefOrRef ImportTypeIfNeeded(this ReferenceImporter importer, ITypeDefOrRef type)
        {
            if (type is TypeSpecification spec)
                return new TypeSpecification(importer.ImportTypeSignature(spec.Signature!));

            return importer.ImportType(type);
        }

        public static bool IsManagedMethodWithBody(this MethodDefinition managedMethod) =>
            managedMethod.Managed && !managedMethod.IsAbstract && !managedMethod.IsPInvokeImpl
            && !managedMethod.IsInternalCall && !managedMethod.IsNative && !managedMethod.IsRuntime;
    }
}
