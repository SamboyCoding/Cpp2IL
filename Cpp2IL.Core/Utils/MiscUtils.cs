using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Reflection;
using Mono.Cecil;
using Mono.Cecil.Rocks;

namespace Cpp2IL.Core.Utils
{
    public static class MiscUtils
    {
        private static readonly object PointerReadLock = new();
        //Disable these because they're initialised in BuildPrimitiveMappings
        // ReSharper disable NotNullMemberIsNotInitialized
#pragma warning disable 8618
        internal static TypeDefinition ObjectReference;
        internal static TypeDefinition StringReference;
        internal static TypeDefinition Int64Reference;
        internal static TypeDefinition SingleReference;
        internal static TypeDefinition DoubleReference;
        internal static TypeDefinition Int32Reference;
        internal static TypeDefinition UInt32Reference;
        internal static TypeDefinition UInt64Reference;
        internal static TypeDefinition BooleanReference;
        internal static TypeDefinition ArrayReference;
        internal static TypeDefinition IEnumerableReference;
        internal static TypeDefinition ExceptionReference;
#pragma warning restore 8618
        // ReSharper restore NotNullMemberIsNotInitialized

        private static Dictionary<string, TypeDefinition> _primitiveTypeMappings = new();
        private static readonly Dictionary<string, Tuple<TypeDefinition?, string[]>> CachedTypeDefsByName = new();
        
        private static List<ulong>? _allKnownFunctionStarts;

        private static readonly Dictionary<string, ulong> PrimitiveSizes = new(14)
        {
            { "Byte", 1 },
            { "SByte", 1 },
            { "Boolean", 1 },
            { "Int16", 2 },
            { "UInt16", 2 },
            { "Char", 2 },
            { "Int32", 4 },
            { "UInt32", 4 },
            { "Single", 4 },
            { "Int64", 8 },
            { "UInt64", 8 },
            { "Double", 8 },
            { "IntPtr", LibCpp2IlMain.Binary!.is32Bit ? 4UL : 8UL },
            { "UIntPtr", LibCpp2IlMain.Binary.is32Bit ? 4UL : 8UL },
        };

        internal static void Reset()
        {
            _primitiveTypeMappings.Clear();
            CachedTypeDefsByName.Clear();
            CecilExtensions.AssignabilityCache.Clear();
            _allKnownFunctionStarts = null;
        }

        public static void BuildPrimitiveMappings()
        {
            ObjectReference = TryLookupTypeDefKnownNotGeneric("System.Object")!;
            StringReference = TryLookupTypeDefKnownNotGeneric("System.String")!;
            Int64Reference = TryLookupTypeDefKnownNotGeneric("System.Int64")!;
            SingleReference = TryLookupTypeDefKnownNotGeneric("System.Single")!;
            DoubleReference = TryLookupTypeDefKnownNotGeneric("System.Double")!;
            Int32Reference = TryLookupTypeDefKnownNotGeneric("System.Int32")!;
            UInt32Reference = TryLookupTypeDefKnownNotGeneric("System.UInt32")!;
            UInt64Reference = TryLookupTypeDefKnownNotGeneric("System.UInt64")!;
            BooleanReference = TryLookupTypeDefKnownNotGeneric("System.Boolean")!;
            ArrayReference = TryLookupTypeDefKnownNotGeneric("System.Array")!;
            IEnumerableReference = TryLookupTypeDefKnownNotGeneric("System.Collections.IEnumerable")!;
            ExceptionReference = TryLookupTypeDefKnownNotGeneric("System.Exception")!;

            _primitiveTypeMappings = new Dictionary<string, TypeDefinition>
            {
                { "string", StringReference },
                { "long", Int64Reference },
                { "float", SingleReference },
                { "double", DoubleReference },
                { "int", Int32Reference },
                { "bool", BooleanReference },
                { "uint", UInt32Reference },
                { "ulong", UInt64Reference }
            };
        }

        public static bool IsManagedTypeAnInstanceOfCppOne(Il2CppTypeReflectionData cppType, TypeReference? managedType)
        {
            if (managedType == null)
                return false;

            if (!cppType.isType && !cppType.isArray && !cppType.isGenericType)
                return false;

            if (cppType.isType && !cppType.isGenericType)
            {
                var managedBaseType = SharedState.UnmanagedToManagedTypes[cppType.baseType!];

                return managedBaseType.IsAssignableFrom(managedType);
            }

            //todo generics etc.

            return false;
        }

        public static bool AreManagedAndCppTypesEqual(Il2CppTypeReflectionData cppType, TypeReference managedType)
        {
            if (!cppType.isType && !cppType.isArray && !cppType.isGenericType) return false;

            if (cppType.baseType!.Name != managedType.Name)
                return false;

            if (cppType.isType && !cppType.isGenericType)
            {
                if (managedType.IsGenericInstance)
                {
                    return AreManagedAndCppTypesEqual(cppType, ((GenericInstanceType)managedType).ElementType);
                }

                return cppType.baseType.FullName == managedType.FullName;
            }

            if (cppType.isType && cppType.isGenericType)
            {
                if (!managedType.HasGenericParameters || managedType.GenericParameters.Count != cppType.genericParams.Length) return false;

                // for (var i = 0; i < managedType.GenericParameters.Count; i++)
                // {
                //     if (managedType.GenericParameters[i].FullName != cppType.genericParams[i].ToString())
                //         return false;
                // }

                return true;
            }

            return false;
        }

        public static bool IsNumericType(TypeReference? reference)
        {
            var def = reference?.Resolve();
            if (def == null) return false;

            return def == Int32Reference || def == Int64Reference || def == SingleReference || def == DoubleReference || def == UInt32Reference;
        }

        public static TypeReference ImportTypeInto(MemberReference importInto, Il2CppType toImport)
        {
            var theDll = LibCpp2IlMain.Binary!;
            var metadata = LibCpp2IlMain.TheMetadata!;

            var moduleDefinition = importInto.Module ?? importInto.DeclaringType?.Module;

            if (moduleDefinition == null)
                throw new Exception($"Couldn't get a module for {importInto}. Its module is {importInto.Module}, its declaring type is {importInto.DeclaringType}, and its declaring type's module is {importInto.DeclaringType?.Module}");

            switch (toImport.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                    return moduleDefinition.ImportReference(TryLookupTypeDefKnownNotGeneric("System.Object"));
                case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                    return moduleDefinition.ImportReference(TryLookupTypeDefKnownNotGeneric("System.Void"));
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                    return moduleDefinition.ImportReference(BooleanReference);
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                    return moduleDefinition.ImportReference(TryLookupTypeDefKnownNotGeneric("System.Char"));
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                    return moduleDefinition.ImportReference(TryLookupTypeDefKnownNotGeneric("System.SByte"));
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                    return moduleDefinition.ImportReference(TryLookupTypeDefKnownNotGeneric("System.Byte"));
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                    return moduleDefinition.ImportReference(TryLookupTypeDefKnownNotGeneric("System.Int16"));
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                    return moduleDefinition.ImportReference(TryLookupTypeDefKnownNotGeneric("System.UInt16"));
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                    return moduleDefinition.ImportReference(Int32Reference);
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                    return moduleDefinition.ImportReference(UInt32Reference);
                case Il2CppTypeEnum.IL2CPP_TYPE_I:
                    return moduleDefinition.ImportReference(TryLookupTypeDefKnownNotGeneric("System.IntPtr"));
                case Il2CppTypeEnum.IL2CPP_TYPE_U:
                    return moduleDefinition.ImportReference(TryLookupTypeDefKnownNotGeneric("System.UIntPtr"));
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                    return moduleDefinition.ImportReference(Int64Reference);
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                    return moduleDefinition.ImportReference(TryLookupTypeDefKnownNotGeneric("System.UInt64"));
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                    return moduleDefinition.ImportReference(SingleReference);
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                    return moduleDefinition.ImportReference(TryLookupTypeDefKnownNotGeneric("System.Double"));
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                    return moduleDefinition.ImportReference(StringReference);
                case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                    return moduleDefinition.ImportReference(TryLookupTypeDefKnownNotGeneric("System.TypedReference"));
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                {
                    var typeDefinition = SharedState.TypeDefsByIndex[toImport.data.classIndex];
                    return moduleDefinition.ImportReference(typeDefinition);
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                {
                    var arrayType = theDll.ReadClassAtVirtualAddress<Il2CppArrayType>(toImport.data.array);
                    var oriType = theDll.GetIl2CppTypeFromPointer(arrayType.etype);
                    return new ArrayType(ImportTypeInto(importInto, oriType), arrayType.rank);
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                {
                    var genericClass =
                        theDll.ReadClassAtVirtualAddress<Il2CppGenericClass>(toImport.data.generic_class);
                    TypeDefinition typeDefinition;
                    if (LibCpp2IlMain.MetadataVersion < 27f)
                    {
                        typeDefinition = SharedState.TypeDefsByIndex[genericClass.typeDefinitionIndex];
                    }
                    else
                    {
                        //V27 - type indexes are pointers now.
                        var type = theDll.ReadClassAtVirtualAddress<Il2CppType>((ulong)genericClass.typeDefinitionIndex);
                        type.Init();
                        typeDefinition = ImportTypeInto(importInto, type).Resolve();
                    }

                    var genericInstanceType = new GenericInstanceType(moduleDefinition.ImportReference(typeDefinition));
                    var genericInst = theDll.ReadClassAtVirtualAddress<Il2CppGenericInst>(genericClass.context.class_inst);
                    ulong[] pointers;

                    lock (PointerReadLock)
                        pointers = theDll.GetPointers(genericInst.pointerStart, (long)genericInst.pointerCount);

                    foreach (var pointer in pointers)
                    {
                        var oriType = theDll.GetIl2CppTypeFromPointer(pointer);
                        genericInstanceType.GenericArguments.Add(ImportTypeInto(importInto, oriType));
                    }

                    return genericInstanceType;
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                {
                    var oriType = theDll.GetIl2CppTypeFromPointer(toImport.data.type);
                    return new ArrayType(ImportTypeInto(importInto, oriType));
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                {
                    if (SharedState.GenericParamsByIndex.TryGetValue(toImport.data.genericParameterIndex, out var genericParameter))
                    {
                        // if (importInto is MethodDefinition mDef)
                        // {
                        //     mDef.GenericParameters.Add(genericParameter);
                        //     mDef.DeclaringType.GenericParameters[mDef.DeclaringType.GenericParameters.IndexOf(genericParameter)] = genericParameter;
                        // }

                        return genericParameter;
                    }

                    var param = metadata.genericParameters[toImport.data.genericParameterIndex];
                    var genericName = metadata.GetStringFromIndex(param.nameIndex);
                    if (importInto is MethodDefinition methodDefinition)
                    {
                        genericParameter = new GenericParameter(genericName, methodDefinition.DeclaringType).WithFlags(param.flags);

                        SharedState.GenericParamsByIndex.Add(toImport.data.genericParameterIndex, genericParameter);

                        param.ConstraintTypes!
                            .Select(c => new GenericParameterConstraint(ImportTypeInto(importInto, c)))
                            .ToList()
                            .ForEach(genericParameter.Constraints.Add);

                        methodDefinition.GenericParameters.Add(genericParameter);
                        methodDefinition.DeclaringType.GenericParameters.Add(genericParameter);
                        return genericParameter;
                    }

                    var typeDefinition = (TypeDefinition)importInto;

                    genericParameter = new GenericParameter(genericName, typeDefinition).WithFlags(param.flags);

                    SharedState.GenericParamsByIndex.Add(toImport.data.genericParameterIndex, genericParameter);

                    typeDefinition.GenericParameters.Add(genericParameter);

                    param.ConstraintTypes!
                        .Select(c => new GenericParameterConstraint(ImportTypeInto(importInto, c)))
                        .ToList()
                        .ForEach(genericParameter.Constraints.Add);

                    return genericParameter;
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                {
                    if (SharedState.GenericParamsByIndex.TryGetValue(toImport.data.genericParameterIndex,
                        out var genericParameter))
                    {
                        return genericParameter;
                    }

                    var methodDefinition = (MethodDefinition)importInto;
                    var param = metadata.genericParameters[toImport.data.genericParameterIndex];
                    var genericName = metadata.GetStringFromIndex(param.nameIndex);
                    genericParameter = new GenericParameter(genericName, methodDefinition).WithFlags(param.flags);

                    SharedState.GenericParamsByIndex.Add(toImport.data.genericParameterIndex, genericParameter);

                    methodDefinition.GenericParameters.Add(genericParameter);

                    param.ConstraintTypes!
                        .Select(c => new GenericParameterConstraint(ImportTypeInto(importInto, c)))
                        .ToList()
                        .ForEach(genericParameter.Constraints.Add);

                    return genericParameter;
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                {
                    var oriType = theDll.GetIl2CppTypeFromPointer(toImport.data.type);
                    return new PointerType(ImportTypeInto(importInto, oriType));
                }

                default:
                    return moduleDefinition.ImportReference(TryLookupTypeDefKnownNotGeneric("System.Object"));
            }
        }


        public static TypeDefinition? TryLookupTypeDefKnownNotGeneric(string? name) => TryLookupTypeDefByName(name).Item1;
        public static Tuple<TypeDefinition?, string[]> TryLookupTypeDefByName(string? name)
        {
            if (name == null) return new Tuple<TypeDefinition?, string[]>(null, Array.Empty<string>());

            var key = name.ToLower(CultureInfo.InvariantCulture);

            if (CachedTypeDefsByName.TryGetValue(key, out var ret))
                return ret;

            var result = InternalTryLookupTypeDefByName(name);

            CachedTypeDefsByName[key] = result;

            return result;
        }

        private static Tuple<TypeDefinition?, string[]> InternalTryLookupTypeDefByName(string name)
        {
            if (_primitiveTypeMappings.ContainsKey(name))
                return new Tuple<TypeDefinition?, string[]>(_primitiveTypeMappings[name], Array.Empty<string>());

            var definedType = SharedState.AllTypeDefinitions.Find(t => string.Equals(t?.FullName, name, StringComparison.OrdinalIgnoreCase));

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
                genericParams = GetGenericParams(name[(name.IndexOf("<", StringComparison.Ordinal) + 1)..^1]);
                name = name[..name.IndexOf("<", StringComparison.Ordinal)];
                if (!name.Contains("`"))
                    name = name + "`" + (genericParams.Length);

                definedType = SharedState.AllTypeDefinitions.Find(t => t.FullName == name);
            }

            if (definedType != null) return new Tuple<TypeDefinition?, string[]>(definedType, genericParams);

            //It's possible they didn't specify a `System.` prefix
            var searchString = $"System.{name}";
            definedType = SharedState.AllTypeDefinitions.Find(t => string.Equals(t.FullName, searchString, StringComparison.OrdinalIgnoreCase));

            if (definedType != null) return new Tuple<TypeDefinition?, string[]>(definedType, genericParams);

            //Still not got one? Ok, is there only one match for non FQN?
            var matches = SharedState.AllTypeDefinitions.Where(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1)
                definedType = matches.First();

            if (definedType != null || !name.Contains(".")) return new Tuple<TypeDefinition?, string[]>(definedType, genericParams);

            searchString = name;
            //Try subclasses
            matches = SharedState.AllTypeDefinitions.Where(t => t.FullName.Replace('/', '.').EndsWith(searchString)).ToList();
            if (matches.Count == 1)
                definedType = matches.First();


            return new Tuple<TypeDefinition?, string[]>(definedType, genericParams);
        }

        private static string[] GetGenericParams(string input)
        {
            if (!input.Contains('<'))
                return input.Split(',');

            var depth = 0;
            var ret = new List<string>();
            var sb = new StringBuilder();

            foreach (var c in input)
            {
                if (c == '<')
                    depth++;
                if (c == '>')
                    depth--;
                if (depth == 0 && c == ',')
                {
                    ret.Add(sb.ToString());
                    sb.Clear();
                    continue;
                }

                sb.Append(c);
            }
            ret.Add(sb.ToString());

            return ret.ToArray();
        }

        private static TypeReference MakeGenericType(this TypeReference self, params TypeReference[] arguments)
        {
            var actualParams = self.GenericParameters.Where(p => p.Type == GenericParameterType.Type).ToList();

            if (actualParams.Count != arguments.Length)
                throw new ArgumentException($"Trying to create generic instance of type {self}, which expects {actualParams.Count} generic parameter(s) ({actualParams.ToStringEnumerable()}), but provided {arguments.Length} argument(s) ({arguments.ToStringEnumerable()})");

            var instance = new GenericInstanceType(self);
            foreach (var argument in arguments)
                instance.GenericArguments.Add(argument);

            return instance;
        }

        public static MethodReference MakeMethodOnGenericType(this MethodReference self, params TypeReference[] typeParams)
        {
            var reference = new MethodReference(self.Name, self.ReturnType)
            {
                DeclaringType = self.DeclaringType.MakeGenericType(typeParams),
                HasThis = self.HasThis,
                ExplicitThis = self.ExplicitThis,
                CallingConvention = self.CallingConvention
            };

            foreach (var parameter in self.Parameters)
                reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));

            foreach (var genericParameter in self.GenericParameters)
                reference.GenericParameters.Add(new GenericParameter(genericParameter.Name, reference));

            return reference;
        }

        public static string? TryGetLiteralAt(Il2CppBinary theDll, ulong rawAddr)
        {
            if (theDll.RawLength <= (long)rawAddr)
                return null;

            var c = Convert.ToChar(theDll.GetByteAtRawAddress(rawAddr));
            if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c) || char.IsWhiteSpace(c))
            {
                var isUnicode = theDll.GetByteAtRawAddress(rawAddr + 1) == 0;
                var literal = new StringBuilder();
                while ((theDll.GetByteAtRawAddress(rawAddr) != 0 || isUnicode && theDll.GetByteAtRawAddress(rawAddr + 1) != 0) && literal.Length < 5000)
                {
                    literal.Append(Convert.ToChar(theDll.GetByteAtRawAddress(rawAddr)));
                    rawAddr++;
                    if (isUnicode) rawAddr++;
                }

                var wasNullTerminated = theDll.GetByteAtRawAddress(rawAddr) == 0;

                if (literal.Length >= 4 || (wasNullTerminated))
                {
                    return literal.ToString();
                }
            }
            else if (c == '\0')
                return string.Empty;

            return null;
        }

        public static bool ShouldBeInFloatingPointRegister(this TypeReference? type)
        {
            if (type == null) return false;

            switch (type.Name)
            {
                case "Single":
                case "Double":
                    return true;
                default:
                    return false;
            }
        }

        public static ulong GetSizeOfObject(TypeReference type)
        {
            if (type.IsValueType && !type.IsPrimitive && type.Resolve() is { } def)
            {
                //Struct - sum instance fields, including any nested structs.
                return (ulong)def.Fields
                    .Where(f => !f.IsStatic)
                    .Select(f => f.FieldType)
                    .Select(reference =>
                        reference == type
                            ? throw new Exception($"Cannot get size of a self-referencing value type: {type} has field of type {reference}")
                            : GetSizeOfObject(reference))
                    .Select(u => (long)u)
                    .Sum();
            }

            return PrimitiveSizes.TryGetValue(type.Name, out var result)
                ? result
                : PrimitiveSizes["IntPtr"];
        }

        public static TypeReference GetUltimateElementType(this ArrayType arr) =>
            arr.ElementType switch
            {
                ArrayType arr2 => arr2.GetUltimateElementType(),
                { } other => other,
            };

        public static int GetSlotNum(int offset)
        {
            var offsetInVtable = offset - Il2CppClassUsefulOffsets.VTABLE_OFFSET; //0x128 being the address of the vtable in an Il2CppClass

            if (offsetInVtable % 0x10 != 0 && offsetInVtable % 0x8 == 0)
                offsetInVtable -= 0x8; //Handle read of the second pointer in the struct.

            if (offsetInVtable > 0)
            {
                var slotNum = (decimal)offsetInVtable / 0x10;

                return (int)slotNum;
            }

            return -1;
        }

        public static int GetPointerSizeBytes()
        {
            return LibCpp2IlMain.Binary!.is32Bit ? 4 : 8;
        }

        public static object GetNumericConstant(ulong addr, TypeReference type)
        {
            var rawAddr = LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(addr);
            var bytes = LibCpp2IlMain.Binary.ReadByteArrayAtRawAddress(rawAddr, (int)GetSizeOfObject(type));

            if (type == Int32Reference)
                return BitConverter.ToInt32(bytes, 0);

            if (type == Int64Reference)
                return BitConverter.ToInt64(bytes, 0);

            if (type == SingleReference)
                return BitConverter.ToSingle(bytes, 0);

            if (type == DoubleReference)
                return BitConverter.ToDouble(bytes, 0);

            throw new ArgumentException("Do not know how to get a numeric constant of type " + type);
        }

        public static TypeReference? TryResolveTypeReflectionData(Il2CppTypeReflectionData? typeData) => TryResolveTypeReflectionData(typeData, null);

        public static TypeReference? TryResolveTypeReflectionData(Il2CppTypeReflectionData? typeData, IGenericParameterProvider? owner, params IGenericParameterProvider?[] extra)
        {
            if (typeData == null)
                return null;

            TypeReference? theType;
            if (!typeData.isArray && typeData.isType && !typeData.isGenericType)
            {
                theType = SharedState.UnmanagedToManagedTypes[typeData.baseType!];
            }
            else if (typeData.isGenericType)
            {
                //TODO TryGetValue this.
                var baseType = SharedState.UnmanagedToManagedTypes[typeData.baseType!];

                var genericType = baseType.MakeGenericType(typeData.genericParams.Select(a => TryResolveTypeReflectionData(a, baseType, extra.Append(owner).ToArray())).ToArray()!);

                theType = genericType;
            }
            else if (typeData.isArray)
            {
                theType = TryResolveTypeReflectionData(typeData.arrayType, owner);

                for (var i = 0; i < typeData.arrayRank; i++)
                {
                    theType = theType.MakeArrayType();
                }
            }
            else
            {
                if (owner == null)
                    throw new ArgumentException($"Owner is null but reflection data {typeData} is a generic parameter, so needs an owner context.", nameof(owner));

                if (owner.GenericParameters.FirstOrDefault(a => a.Name == typeData.variableGenericParamName) is { } gp)
                    return gp;

                foreach (var extraProvider in extra)
                {
                    if (extraProvider?.GenericParameters.FirstOrDefault(a => a.Name == typeData.variableGenericParamName) is { } gp2)
                        return gp2;
                }

                //Generic parameter
                theType = new GenericParameter(typeData.variableGenericParamName, owner);
            }

            return theType;
        }


        public static GenericInstanceMethod MakeGenericMethodFromType(MethodReference managedMethod, GenericInstanceType git)
        {
            var gim = new GenericInstanceMethod(managedMethod);
            foreach (var gitGenericArgument in git.GenericArguments)
            {
                gim.GenericArguments.Add(gitGenericArgument);
            }

            return gim;
        }

        public static bool TryCoerceToUlong(object value, out ulong ret)
        {
            ret = 0;
            try
            {
                ret = (ulong) AnalysisUtils.CoerceValue(value, UInt64Reference)!;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static IConvertible ReinterpretBytes(IConvertible original, TypeReference desired) => ReinterpretBytes(original, typeof(int).Module.GetType((desired is TypeSpecification typeSpec ? typeSpec.ElementType : desired).FullName));

        public static IConvertible ReinterpretBytes(IConvertible original, Type desired)
        {
            if (desired is null)
                throw new ArgumentNullException(nameof(desired), "Destination type is null");
            
            var rawBytes = original switch
            {
                bool b => BitConverter.GetBytes(b),
                char c => BitConverter.GetBytes(c),
                byte b => BitConverter.GetBytes(b),
                sbyte sb => BitConverter.GetBytes(sb),
                ushort us => BitConverter.GetBytes(us),
                short s => BitConverter.GetBytes(s),
                uint ui => BitConverter.GetBytes(ui),
                int i => BitConverter.GetBytes(i),
                ulong ul => BitConverter.GetBytes(ul),
                long l => BitConverter.GetBytes(l),
                float f => BitConverter.GetBytes(f),
                double d => BitConverter.GetBytes(d),
                _ => throw new($"ReinterpretBytes: Cannot get byte array from {original} (type {original.GetType()}")
            };
            
            //Pad out with leading zeros if we have to
            var requiredLength = LibCpp2ILUtils.VersionAwareSizeOf(desired);

            if (requiredLength > rawBytes.Length)
            {
                rawBytes = ((byte) 0).Repeat(requiredLength - rawBytes.Length).Concat(rawBytes).ToArray();
            }

            if (desired == typeof(bool))
                return BitConverter.ToBoolean(rawBytes, 0);
            if (desired == typeof(byte))
                return rawBytes[0];
            if (desired == typeof(char))
                return BitConverter.ToChar(rawBytes, 0);
            if (desired == typeof(sbyte))
                return unchecked((sbyte)rawBytes[0]);
            if (desired == typeof(ushort))
                return BitConverter.ToUInt16(rawBytes, 0);
            if (desired == typeof(short))
                return BitConverter.ToInt16(rawBytes,0);
            if (desired == typeof(uint))
                return BitConverter.ToUInt32(rawBytes, 0);
            if (desired == typeof(int))
                return BitConverter.ToInt32(rawBytes, 0);
            if (desired == typeof(ulong))
                return BitConverter.ToUInt64(rawBytes, 0);
            if (desired == typeof(long))
                return BitConverter.ToInt64(rawBytes, 0);
            if (desired == typeof(float))
                return BitConverter.ToSingle(rawBytes, 0);
            if(desired == typeof(double))
                return BitConverter.ToDouble(rawBytes, 0);

            throw new($"ReinterpretBytes: Cannot convert byte array back to a type of {desired}");
        }

        private static void InitFunctionStarts()
        {
            _allKnownFunctionStarts = LibCpp2IlMain.TheMetadata!.methodDefs.Select(m => m.MethodPointer)
                .Concat(LibCpp2IlMain.Binary!.ConcreteGenericImplementationsByAddress.Keys)
                .Concat(SharedState.AttributeGeneratorStarts)
                .ToList();

            //Sort in ascending order
            _allKnownFunctionStarts.Sort();
        }

        public static ulong GetAddressOfNextFunctionStart(ulong current)
        {
            if(_allKnownFunctionStarts == null)
                InitFunctionStarts();

            //Binary-search-like approach
            var lower = 0;
            var upper = _allKnownFunctionStarts!.Count - 1;

            var ret = ulong.MaxValue;
            while (upper - lower >= 1)
            {
                var pos = (upper - lower) / 2 + lower;

                if (upper - lower == 1)
                    pos = lower;

                var ptr = _allKnownFunctionStarts[pos];
                if (ptr > current)
                {
                    //This matches what we want to look for
                    if (ptr < ret)
                        //This is a better "next method" pointer
                        ret = ptr;

                    //Either way, we're above our current address now, so search lower in the list
                    upper = pos;
                }
                else
                {
                    //Not what we want, so move up in the list
                    lower = pos + 1;
                }
            }
            ret = _allKnownFunctionStarts[lower];
            if (ret < current)
                ret = _allKnownFunctionStarts[upper];

            if (ret <= current && upper == _allKnownFunctionStarts.Count - 1)
                return 0;

            return ret;
        }
    }
}
