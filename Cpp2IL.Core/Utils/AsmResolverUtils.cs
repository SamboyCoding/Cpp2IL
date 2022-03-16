using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
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
        private static readonly ConcurrentDictionary<AssemblyDefinition, ReferenceImporter> ImportersByAssembly = new();

        internal static readonly ConcurrentDictionary<long, TypeDefinition> TypeDefsByIndex = new();
        internal static readonly Dictionary<long, GenericParameter> GenericParamsByIndexNew = new();

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
                _ => throw new ArgumentException("Type is not a primitive", nameof(type))
            };

        public static TypeSignature GetTypeSignatureFromIl2CppType(ModuleDefinition module, Il2CppType il2CppType)
        {
            //Module is needed for generic params
            if (il2CppType == null)
                throw new ArgumentNullException(nameof(il2CppType));

            var theDll = LibCpp2IlMain.Binary!;

            switch (il2CppType.type)
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
                    return GetPrimitiveTypeDef(il2CppType.type).ToTypeSignature();
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                    return TypeDefsByIndex[il2CppType.data.classIndex].ToTypeSignature();
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                    var arrayType = theDll.ReadClassAtVirtualAddress<Il2CppArrayType>(il2CppType.data.array);
                    var elementType = theDll.GetIl2CppTypeFromPointer(arrayType.etype);
                    return GetTypeSignatureFromIl2CppType(module, elementType).MakeArrayType(arrayType.rank);
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    return GetTypeSignatureFromIl2CppType(module, theDll.GetIl2CppTypeFromPointer(il2CppType.data.type))
                        .MakePointerType();
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR: //Generic type parameter
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR: //Generic method parameter
                    var il2CppGenericParameter = LibCpp2IlMain.TheMetadata!.genericParameters[il2CppType.data.genericParameterIndex];
                    return new GenericParameterSignature(
                        module,
                        il2CppType.type is Il2CppTypeEnum.IL2CPP_TYPE_MVAR ? GenericParameterType.Method : GenericParameterType.Type,
                        il2CppGenericParameter.genericParameterIndexInOwner
                    );
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    var szArrayElementType = theDll.GetIl2CppTypeFromPointer(il2CppType.data.type);

                    return GetTypeSignatureFromIl2CppType(module, szArrayElementType).MakeSzArrayType();
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                {
                    var genericClass = theDll.ReadClassAtVirtualAddress<Il2CppGenericClass>(il2CppType.data.generic_class);
                    TypeDefsByIndex.TryGetValue(genericClass.typeDefinitionIndex, out var typeDefinition);
                    if (LibCpp2IlMain.MetadataVersion >= 27f)
                    {
                        //V27 - type indexes are pointers now.
                        var type = theDll.ReadClassAtVirtualAddress<Il2CppType>((ulong) genericClass.typeDefinitionIndex);
                        type.Init();
                        typeDefinition = GetTypeSignatureFromIl2CppType(module, type).Resolve() ?? throw new Exception("Unable to resolve base type for generic inst");
                    }

                    var importer = module.Assembly!.GetImporter();

                    var genericInstanceType = new GenericInstanceTypeSignature(importer.ImportType(typeDefinition!), typeDefinition!.IsValueType);
                    var genericInst = theDll.ReadClassAtVirtualAddress<Il2CppGenericInst>(genericClass.context.class_inst);

                    // lock (PointerReadLock)
                    var pointers = theDll.GetPointers(genericInst.pointerStart, (long) genericInst.pointerCount);

                    foreach (var pointer in pointers)
                    {
                        var gpSig = GetTypeSignatureFromIl2CppType(module, theDll.GetIl2CppTypeFromPointer(pointer));
                        genericInstanceType.TypeArguments.Add(importer.ImportTypeSignature(gpSig));
                    }

                    return genericInstanceType;
                }
                default:
                    throw new("Don't know how to make a type signature from " + il2CppType.type);
            }
        }

        public static ITypeDescriptor GetTypeDefFromIl2CppType(ReferenceImporter importer, Il2CppType il2CppType)
        {
            if (il2CppType == null)
                throw new ArgumentNullException(nameof(il2CppType));

            var theDll = LibCpp2IlMain.Binary!;

            switch (il2CppType.type)
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
                    return importer.ImportType(GetPrimitiveTypeDef(il2CppType.type).ToTypeReference());
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                {
                    var typeDefinition = TypeDefsByIndex[il2CppType.data.classIndex];
                    return importer.ImportType(typeDefinition.ToTypeReference());
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                {
                    var arrayType = theDll.ReadClassAtVirtualAddress<Il2CppArrayType>(il2CppType.data.array);
                    var oriType = theDll.GetIl2CppTypeFromPointer(arrayType.etype);
                    return importer.ImportTypeIfNeeded(GetTypeDefFromIl2CppType(importer, oriType).ToTypeDefOrRef()).MakeArrayType(arrayType.rank);
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                {
                    var genericClass = theDll.ReadClassAtVirtualAddress<Il2CppGenericClass>(il2CppType.data.generic_class);
                    TypeDefinition typeDefinition;
                    if (LibCpp2IlMain.MetadataVersion < 27f)
                    {
                        typeDefinition = TypeDefsByIndex[genericClass.typeDefinitionIndex];
                    }
                    else
                    {
                        //V27 - type indexes are pointers now.
                        var type = theDll.ReadClassAtVirtualAddress<Il2CppType>((ulong) genericClass.typeDefinitionIndex);
                        type.Init();
                        typeDefinition = GetTypeDefFromIl2CppType(importer, type).Resolve() ?? throw new Exception("Unable to resolve base type for generic inst");
                    }

                    var genericInstanceType = new GenericInstanceTypeSignature(importer.ImportType(typeDefinition.ToTypeReference()), typeDefinition.IsValueType);
                    var genericInst = theDll.ReadClassAtVirtualAddress<Il2CppGenericInst>(genericClass.context.class_inst);
                    ulong[] pointers;

                    lock (PointerReadLock)
                        pointers = theDll.GetPointers(genericInst.pointerStart, (long) genericInst.pointerCount);

                    foreach (var pointer in pointers)
                    {
                        var oriType = theDll.GetIl2CppTypeFromPointer(pointer);
                        if (oriType.type is not Il2CppTypeEnum.IL2CPP_TYPE_VAR and not Il2CppTypeEnum.IL2CPP_TYPE_MVAR)
                            genericInstanceType.TypeArguments.Add(importer.ImportTypeSignature(GetTypeDefFromIl2CppType(importer, oriType).ToTypeSignature()));
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
                    if (oriType.type is not Il2CppTypeEnum.IL2CPP_TYPE_MVAR and not Il2CppTypeEnum.IL2CPP_TYPE_VAR)
                        return importer.ImportTypeIfNeeded(GetTypeDefFromIl2CppType(importer, oriType).ToTypeDefOrRef()).MakeSzArrayType();

                    var gp = LibCpp2IlMain.TheMetadata!.genericParameters[oriType.data.genericParameterIndex];
                    var gpt = oriType.type is Il2CppTypeEnum.IL2CPP_TYPE_MVAR ? GenericParameterType.Method : GenericParameterType.Type;
                    return new GenericParameterSignature(gpt, gp.genericParameterIndexInOwner).MakeSzArrayType();
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                {
                    var gp = LibCpp2IlMain.TheMetadata!.genericParameters[il2CppType.data.genericParameterIndex];
                    var gpt = il2CppType.type is Il2CppTypeEnum.IL2CPP_TYPE_MVAR ? GenericParameterType.Method : GenericParameterType.Type;
                    return new GenericParameterSignature(importer.TargetModule, gpt, gp.genericParameterIndexInOwner);
                }
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                {
                    var oriType = theDll.GetIl2CppTypeFromPointer(il2CppType.data.type);
                    return importer.ImportType(GetTypeDefFromIl2CppType(importer, oriType).ToTypeDefOrRef()).MakePointerType();
                }

                default:
                    return importer.ImportType(TypeDefinitionsAsmResolver.Object.ToTypeReference());
            }
        }

        public static GenericParameter ImportGenericParameter(Il2CppType il2CppType, ReferenceImporter importer, bool isMethod)
        {
            switch (il2CppType.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                {
                    if (GenericParamsByIndexNew.TryGetValue(il2CppType.data.genericParameterIndex, out var genericParameter))
                        return genericParameter;

                    var param = LibCpp2IlMain.TheMetadata!.genericParameters[il2CppType.data.genericParameterIndex];
                    var genericName = LibCpp2IlMain.TheMetadata.GetStringFromIndex(param.nameIndex);
                    if (isMethod)
                    {
                        genericParameter = new GenericParameter(genericName, (GenericParameterAttributes) param.flags);

                        GenericParamsByIndexNew.Add(il2CppType.data.genericParameterIndex, genericParameter);

                        param.ConstraintTypes!
                            .Select(c => new GenericParameterConstraint(importer.ImportTypeIfNeeded(GetTypeDefFromIl2CppType(importer, c).ToTypeDefOrRef())))
                            .ToList()
                            .ForEach(genericParameter.Constraints.Add);

                        return genericParameter;
                    }

                    genericParameter = new GenericParameter(genericName, (GenericParameterAttributes) param.flags);

                    GenericParamsByIndexNew.Add(il2CppType.data.genericParameterIndex, genericParameter);

                    param.ConstraintTypes!
                        .Select(c => new GenericParameterConstraint(importer.ImportTypeIfNeeded(GetTypeDefFromIl2CppType(importer, c).ToTypeDefOrRef())))
                        .ToList()
                        .ForEach(genericParameter.Constraints.Add);

                    return genericParameter;
                }
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                {
                    if (GenericParamsByIndexNew.TryGetValue(il2CppType.data.genericParameterIndex, out var genericParameter))
                    {
                        return genericParameter;
                    }

                    var param = LibCpp2IlMain.TheMetadata!.genericParameters[il2CppType.data.genericParameterIndex];
                    var genericName = LibCpp2IlMain.TheMetadata.GetStringFromIndex(param.nameIndex);
                    genericParameter = new GenericParameter(genericName, (GenericParameterAttributes) param.flags);

                    GenericParamsByIndexNew.Add(il2CppType.data.genericParameterIndex, genericParameter);

                    param.ConstraintTypes!
                        .Select(c => new GenericParameterConstraint(importer.ImportTypeIfNeeded(GetTypeDefFromIl2CppType(importer, c).ToTypeDefOrRef())))
                        .ToList()
                        .ForEach(genericParameter.Constraints.Add);

                    return genericParameter;
                }
                default:
                    throw new($"Can't import {il2CppType.type} as a generic param because it isn't one");
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

        public static TypeSignature ImportTypeSignatureIfNeeded(this ReferenceImporter importer, TypeSignature signature) => signature is GenericParameterSignature ? signature : importer.ImportTypeSignature(signature);

        public static ITypeDefOrRef ImportTypeIfNeeded(this ReferenceImporter importer, ITypeDefOrRef type)
        {
            if (type is TypeSpecification spec)
                return new TypeSpecification(importer.ImportTypeSignatureIfNeeded(spec.Signature!));

            return importer.ImportType(type);
        }

        public static ElementType GetElementTypeFromConstant(object? primitive) => primitive is null
            ? ElementType.Object
            : primitive switch
            {
                sbyte => ElementType.I1,
                byte => ElementType.U1,
                bool => ElementType.Boolean,
                short => ElementType.I2,
                ushort => ElementType.U2,
                int => ElementType.I4,
                uint => ElementType.U4,
                long => ElementType.I8,
                ulong => ElementType.U8,
                float => ElementType.R4,
                double => ElementType.R8,
                string => ElementType.String,
                char => ElementType.Char,
                _ => throw new($"Can't get a element type for the constant {primitive} of type {primitive.GetType()}"),
            };

        public static Constant MakeConstant(object from)
        {
            if (from is string s)
                return new(ElementType.String, new(Encoding.Unicode.GetBytes(s)));

            return new(GetElementTypeFromConstant(from), new(MiscUtils.RawBytes((IConvertible) from)));
        }

        public static bool IsManagedMethodWithBody(this MethodDefinition managedMethod) =>
            managedMethod.Managed && !managedMethod.IsAbstract && !managedMethod.IsPInvokeImpl
            && !managedMethod.IsInternalCall && !managedMethod.IsNative && !managedMethod.IsRuntime;
    }
}