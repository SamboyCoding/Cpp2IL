using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Cpp2IL.Analysis.ResultModels;
using Iced.Intel;
using LibCpp2IL;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.PE;
using LibCpp2IL.Reflection;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using SharpDisasm;
using SharpDisasm.Udis86;
using Instruction = SharpDisasm.Instruction;

namespace Cpp2IL
{
    public static class Utils
    {
        private static readonly object _pointerReadLock = new object();
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

        private static Dictionary<string, TypeDefinition> primitiveTypeMappings = new Dictionary<string, TypeDefinition>();
        private static readonly Dictionary<string, Tuple<TypeDefinition?, string[]>> _cachedTypeDefsByName = new Dictionary<string, Tuple<TypeDefinition?, string[]>>();
        private static readonly Dictionary<(TypeDefinition, TypeReference), bool> _assignableCache = new Dictionary<(TypeDefinition, TypeReference), bool>();

        private static readonly Dictionary<string, ulong> PrimitiveSizes = new Dictionary<string, ulong>(14)
        {
            {"Byte", 1},
            {"SByte", 1},
            {"Boolean", 1},
            {"Int16", 2},
            {"UInt16", 2},
            {"Char", 2},
            {"Int32", 4},
            {"UInt32", 4},
            {"Single", 4},
            {"Int64", 8},
            {"UInt64", 8},
            {"Double", 8},
            {"IntPtr", LibCpp2IlMain.Binary!.is32Bit ? 4UL : 8UL},
            {"UIntPtr", LibCpp2IlMain.Binary!.is32Bit ? 4UL : 8UL},
        };

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

            primitiveTypeMappings = new Dictionary<string, TypeDefinition>
            {
                {"string", StringReference},
                {"long", Int64Reference},
                {"float", SingleReference},
                {"double", DoubleReference},
                {"int", Int32Reference},
                {"bool", BooleanReference},
                {"uint", UInt32Reference},
                {"ulong", UInt64Reference}
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

                return CheckAssignability(managedBaseType, managedType);
            }

            //todo generics etc.

            return false;
        }

        private static bool CheckAssignability(TypeDefinition baseType, TypeReference potentialChild)
        {
            var key = (baseType, potentialChild);
            if (!_assignableCache.ContainsKey(key))
            {
                _assignableCache[key] = baseType.IsAssignableFrom(potentialChild);
            }

            return _assignableCache[key];
        }
        
        public static bool AreManagedAndCppTypesEqual(Il2CppTypeReflectionData cppType, TypeReference managedType)
        {
            if (!cppType.isType && !cppType.isArray && !cppType.isGenericType) return false;

            if (cppType.baseType.Name != managedType.Name)
                return false;

            if (cppType.isType && !cppType.isGenericType)
            {
                if (managedType.IsGenericInstance)
                {
                    return AreManagedAndCppTypesEqual(cppType, ((GenericInstanceType) managedType).ElementType);
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

        public static bool IsNumericType(TypeReference reference)
        {
            var def = reference.Resolve();
            if (def == null) return false;

            return def == Int32Reference || def == Int64Reference || def == SingleReference || def == DoubleReference || def == UInt32Reference;
        }

        public static TypeReference ImportTypeInto(MemberReference importInto, Il2CppType toImport)
        {
            var theDll = LibCpp2IlMain.Binary!;
            var metadata = LibCpp2IlMain.TheMetadata!;

            var moduleDefinition = importInto.Module;
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
                        var type = theDll.ReadClassAtVirtualAddress<Il2CppType>((ulong) genericClass.typeDefinitionIndex);
                        type.Init();
                        typeDefinition = ImportTypeInto(importInto, type).Resolve();
                    }

                    var genericInstanceType = new GenericInstanceType(moduleDefinition.ImportReference(typeDefinition));
                    var genericInst = theDll.ReadClassAtVirtualAddress<Il2CppGenericInst>(genericClass.context.class_inst);
                    ulong[] pointers;

                    lock (_pointerReadLock)
                        pointers = theDll.GetPointers(genericInst.pointerStart, (long) genericInst.pointerCount);

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
                        genericParameter = new GenericParameter(genericName, methodDefinition.DeclaringType);
                        methodDefinition.GenericParameters.Add(genericParameter);
                        methodDefinition.DeclaringType.GenericParameters.Add(genericParameter);
                        SharedState.GenericParamsByIndex.Add(toImport.data.genericParameterIndex, genericParameter);
                        return genericParameter;
                    }

                    var typeDefinition = (TypeDefinition) importInto;

                    genericParameter = new GenericParameter(genericName, typeDefinition);
                    typeDefinition.GenericParameters.Add(genericParameter);
                    SharedState.GenericParamsByIndex.Add(toImport.data.genericParameterIndex, genericParameter);
                    return genericParameter;
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                {
                    if (SharedState.GenericParamsByIndex.TryGetValue(toImport.data.genericParameterIndex,
                        out var genericParameter))
                    {
                        return genericParameter;
                    }

                    var methodDefinition = (MethodDefinition) importInto;
                    var param = metadata.genericParameters[toImport.data.genericParameterIndex];
                    var genericName = metadata.GetStringFromIndex(param.nameIndex);
                    genericParameter = new GenericParameter(genericName, methodDefinition);
                    methodDefinition.GenericParameters.Add(genericParameter);
                    SharedState.GenericParamsByIndex.Add(toImport.data.genericParameterIndex, genericParameter);
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

        public static int CheckForInitCallAtIndex(ulong offsetInRam, List<Instruction> instructions, int idx, KeyFunctionAddresses kfe)
        {
            //Refined Targeting Mechanism
            //JNZ to skip the following code if not needed
            //Then MOV (which contains the unique ID of this function)
            //Then a CALL to the init function
            //Then Another MOV
            //The next instruction is where the JNZ would go to and is where we resume.

            var alternativePattern = new[]
            {
                ud_mnemonic_code.UD_Ijnz, ud_mnemonic_code.UD_Imov, ud_mnemonic_code.UD_Icall, ud_mnemonic_code.UD_Imov
            };

            if (instructions.Count - idx < 4) return 0;

            var instructionsInRange = instructions.GetRange(idx, 4);
            var actualPattern = instructionsInRange.Select(i => i.Mnemonic).ToArray();

            if (!alternativePattern.SequenceEqual(actualPattern)) return 0;

            try
            {
                var callAddr = GetJumpTarget(instructionsInRange[2], offsetInRam + instructionsInRange[2].PC);
                return callAddr == kfe.il2cpp_codegen_initialize_method ? 3 : 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        public static int CheckForStaticClassInitAtIndex(ulong offsetInRam, List<Instruction> instructions, int idx, KeyFunctionAddresses kfe)
        {
            var requiredPattern = new[]
            {
                ud_mnemonic_code.UD_Itest, ud_mnemonic_code.UD_Ijz, ud_mnemonic_code.UD_Icmp, ud_mnemonic_code.UD_Ijnz, ud_mnemonic_code.UD_Icall
            };

            var alternativePattern = new[]
            {
                ud_mnemonic_code.UD_Itest, ud_mnemonic_code.UD_Ijz, ud_mnemonic_code.UD_Icmp, ud_mnemonic_code.UD_Ijnz, ud_mnemonic_code.UD_Iadd, ud_mnemonic_code.UD_Ijmp
            };

            var thirdPattern = new[]
            {
                ud_mnemonic_code.UD_Itest, ud_mnemonic_code.UD_Ijz, ud_mnemonic_code.UD_Icmp, ud_mnemonic_code.UD_Ijnz, ud_mnemonic_code.UD_Imov, ud_mnemonic_code.UD_Icall
            };

            if (instructions.Count - idx < 5) return 0;

            var instructionsInRange = instructions.GetRange(idx, 5);
            var actualPattern = instructionsInRange.Select(i => i.Mnemonic).ToArray();
            if (requiredPattern.SequenceEqual(actualPattern))
            {
                var callAddr = GetJumpTarget(instructionsInRange[4], offsetInRam + instructionsInRange[4].PC);

                //If this is true then we have an il2cpp-generated initialization call.
                return callAddr == kfe.il2cpp_runtime_class_init_actual || callAddr == kfe.il2cpp_runtime_class_init_export ? 4 : 0;
            }
            else
            {
                if (instructions.Count - idx < 7) return 0;

                instructionsInRange = instructions.GetRange(idx, 6);
                actualPattern = instructionsInRange.Select(i => i.Mnemonic).ToArray();

                if (!alternativePattern.SequenceEqual(actualPattern) && !thirdPattern.SequenceEqual(actualPattern)) return 0;

                var callAddr = GetJumpTarget(instructionsInRange[5], offsetInRam + instructionsInRange[5].PC);

                //If this is true then we have an il2cpp-generated initialization call.
                return callAddr == kfe.il2cpp_runtime_class_init_actual || callAddr == kfe.il2cpp_runtime_class_init_export ? 5 : 0;
            }
        }


        public static TypeDefinition? TryLookupTypeDefKnownNotGeneric(string? name) => TryLookupTypeDefByName(name).Item1;

        public static Tuple<TypeDefinition?, string[]> TryLookupTypeDefByName(string? name)
        {
            if (name == null) return new Tuple<TypeDefinition?, string[]>(null, Array.Empty<string>());

            var key = name.ToLower(CultureInfo.InvariantCulture);

            if (_cachedTypeDefsByName.TryGetValue(key, out var ret))
                return ret;

            var result = InternalTryLookupTypeDefByName(name);

            _cachedTypeDefsByName[key] = result;

            return result;
        }

        private static Tuple<TypeDefinition?, string[]> InternalTryLookupTypeDefByName(string name)
        {
            if (primitiveTypeMappings.ContainsKey(name))
                return new Tuple<TypeDefinition?, string[]>(primitiveTypeMappings[name], Array.Empty<string>());

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
                var origName = name;
                genericParams = name[(name.IndexOf("<", StringComparison.Ordinal) + 1)..].TrimEnd('>').Split(',');
                name = name[..name.IndexOf("<", StringComparison.Ordinal)];
                if (!name.Contains("`"))
                    name = name + "`" + (origName.Count(c => c == ',') + 1);

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

        public static MethodReference MakeGeneric(this MethodReference self, params TypeReference[] arguments)
        {
            var reference = new MethodReference(self.Name, self.ReturnType)
            {
                DeclaringType = self.DeclaringType.MakeGenericType(arguments),
                HasThis = self.HasThis,
                ExplicitThis = self.ExplicitThis,
                CallingConvention = self.CallingConvention
            };

            foreach (var parameter in self.Parameters)
                reference.Parameters.Add(new ParameterDefinition(parameter.ParameterType));

            foreach (var generic_parameter in self.GenericParameters)
                reference.GenericParameters.Add(new GenericParameter(generic_parameter.Name, reference));

            return reference;
        }

        public static string Repeat(string input, int count)
        {
            var ret = new StringBuilder();
            for (var i = 0; i < count; i++)
            {
                ret.Append(input);
            }

            return ret.ToString();
        }

        public static string InvertCondition(string condition)
        {
            if (condition.Contains("== false"))
                return condition.Replace(" == false", "");
            if (condition.Contains("== true"))
                return "!" + condition.Replace(" == true", "");
            if (condition.Contains("is zero or null"))
                return condition.Replace("is zero or null", "is NOT zero or null");
            if (condition.Contains("is NOT zero or null"))
                return condition.Replace("is NOT zero or null", "is zero or null");
            if (condition.Contains("=="))
                return condition.Replace("==", "!=");
            if (condition.Contains("!="))
                return condition.Replace("!=", "==");
            if (condition.Contains(">="))
                return condition.Replace(">=", "<");
            if (condition.Contains("<="))
                return condition.Replace("<=", ">");
            if (condition.Contains(">"))
                return condition.Replace(">", "<=");
            if (condition.Contains("<"))
                return condition.Replace("<", ">=");

            return condition;
        }

        public static StringBuilder AppendGenerics(this StringBuilder builder, string[] genericParams)
        {
            if (genericParams.Length > 0)
            {
                builder.Append("<");

                foreach (var genericParam in genericParams)
                {
                    builder.Append(genericParam);
                    if (genericParams.Last() != genericParam)
                        builder.Append(", ");
                }

                builder.Append(">");
            }

            return builder;
        }

        // public static bool IsAssignableFrom(this TypeReference? baseClass, TypeReference? subClass)
        // {
        //     if (baseClass == null || subClass == null) return false;
        //
        //     if (subClass is TypeDefinition otherDef)
        //     {
        //         if (baseClass.FullName == otherDef.FullName) return true;
        //
        //         if (otherDef.BaseType != null && baseClass.IsAssignableFrom(TryLookupTypeDefByName(otherDef.BaseType.FullName).Item1))
        //             return true;
        //
        //         if (otherDef.Interfaces.Any(i => baseClass.IsAssignableFrom(i.InterfaceType)))
        //             return true;
        //
        //         return false;
        //     }
        //
        //     return baseClass.FullName == subClass.FullName; //Simple check
        // }

        public static string? TryGetLiteralAt(Il2CppBinary theDll, ulong rawAddr)
        {
            var c = Convert.ToChar(theDll.GetByteAtRawAddress(rawAddr));
            if (char.IsLetter(c) && c < 'z') //includes uppercase
            {
                var isUnicode = theDll.GetByteAtRawAddress(rawAddr + 1) == 0;
                var literal = new StringBuilder();
                while ((theDll.GetByteAtRawAddress(rawAddr) != 0 || isUnicode && theDll.GetByteAtRawAddress(rawAddr + 1) != 0) && literal.Length < 250)
                {
                    literal.Append(Convert.ToChar(theDll.GetByteAtRawAddress(rawAddr)));
                    rawAddr++;
                    if (isUnicode) rawAddr++;
                }


                if (literal.Length > 4)
                {
                    return literal.ToString();
                }
            }

            return null;
        }

        public static bool ShouldBeInFloatingPointRegister(TypeReference? type)
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
                return (ulong) def.Fields
                    .Where(f => !f.IsStatic)
                    .Select(f => f.FieldType)
                    .Select(reference => 
                        reference == type 
                            ? throw new Exception($"Cannot get size of a self-referencing value type: {type} has field of type {reference}") 
                            : GetSizeOfObject(reference))
                    .Select(u => (long) u)
                    .Sum();
            }

            return PrimitiveSizes.TryGetValue(type.Name, out var result)
                ? result
                : PrimitiveSizes["IntPtr"];
        }

        private static readonly Regex UpscaleRegex = new Regex("(?:^|([^a-zA-Z]))e([a-z]{2})", RegexOptions.Compiled);

        private static readonly Dictionary<string, string> _cachedUpscaledRegisters = new Dictionary<string, string>();

        public static string UpscaleRegisters(string replaceIn)
        {
            if (_cachedUpscaledRegisters.ContainsKey(replaceIn))
                return _cachedUpscaledRegisters[replaceIn];

            if (replaceIn.Length < 2) return replaceIn;

            //Special case the few 8-bit register: "al" => "rax" etc
            if (replaceIn == "al")
                return "rax";
            if (replaceIn == "bl")
                return "rbx";
            if (replaceIn == "dl")
                return "rdx";
            if (replaceIn == "ax")
                return "rax";
            if (replaceIn == "cx" || replaceIn == "cl")
                return "rcx";

            //R9d, etc.
            if (replaceIn[0] == 'r' && replaceIn[^1] == 'd')
                return replaceIn.Substring(0, replaceIn.Length - 1);

            _cachedUpscaledRegisters[replaceIn] = UpscaleRegex.Replace(replaceIn, "$1r$2");

            return _cachedUpscaledRegisters[replaceIn];
        }

        public static string GetFloatingRegister(string original)
        {
            switch (original)
            {
                case "rcx":
                    return "xmm0";
                case "rdx":
                    return "xmm1";
                case "r8":
                    return "xmm2";
                case "r9":
                    return "xmm3";
                default:
                    return original;
            }
        }

        private static readonly ConcurrentDictionary<ud_type, string> CachedRegNames = new ConcurrentDictionary<ud_type, string>();
        private static readonly ConcurrentDictionary<Register, string> CachedRegNamesNew = new ConcurrentDictionary<Register, string>();

        public static string GetRegisterNameNew(Register register)
        {
            if (register == Register.None) return "";

            if (!register.IsVectorRegister())
                return register.GetFullRegister().ToString().ToLowerInvariant();

            if (!CachedRegNamesNew.TryGetValue(register, out var ret))
            {
                ret = UpscaleRegisters(register.ToString().ToLower());
                CachedRegNamesNew[register] = ret;
            }

            return ret;
        }

        public static string GetRegisterName(Operand operand)
        {
            var theBase = operand.Base;

            if (theBase == ud_type.UD_NONE) return "";

            if (!CachedRegNames.TryGetValue(theBase, out var ret))
            {
                ret = UpscaleRegisters(theBase.ToString().Replace("UD_R_", "").ToLower());
                CachedRegNames[theBase] = ret;
            }

            return ret;
        }

        public static int GetSlotNum(int offset)
        {
            var offsetInVtable = offset - Il2CppClassUsefulOffsets.VTABLE_OFFSET; //0x128 being the address of the vtable in an Il2CppClass

            if (offsetInVtable % 0x10 != 0 && offsetInVtable % 0x8 == 0)
                offsetInVtable -= 0x8; //Handle read of the second pointer in the struct.

            if (offsetInVtable > 0)
            {
                var slotNum = (decimal) offsetInVtable / 0x10;

                return (int) slotNum;
            }

            return -1;
        }

        public static InstructionList GetMethodBodyAtVirtAddressNew(Il2CppBinary theDll, ulong addr, bool peek)
        {
            var functionStart = addr;
            var ret = new InstructionList();
            var con = true;
            var buff = new List<byte>();
            var rawAddr = theDll.MapVirtualAddressToRaw(addr);

            if (rawAddr < 0 || rawAddr >= theDll.RawLength)
            {
                Console.WriteLine($"Invalid call to GetMethodBodyAtVirtAddressNew, virt addr {addr} resolves to raw {rawAddr} which is out of bounds");
                return ret;
            }

            while (con)
            {
                buff.Add(theDll.GetByteAtRawAddress((ulong) rawAddr));

                ret = LibCpp2ILUtils.DisassembleBytesNew(theDll.is32Bit, buff.ToArray(), functionStart);

                if (ret.All(i => i.Mnemonic != Mnemonic.INVALID) && ret.Any(i => i.Code == Code.Int3))
                    con = false;

                if (peek && buff.Count > 50)
                    con = false;
                else if (buff.Count > 1000)
                    con = false; //Sanity breakout.

                addr++;
                rawAddr++;
            }

            return ret;
        }

        public static List<Instruction> GetMethodBodyAtRawAddress(Il2CppBinary theDll, long addr, bool peek)
        {
            var ret = new List<Instruction>();
            var con = true;
            var buff = new List<byte>();
            while (con)
            {
                buff.Add(theDll.GetByteAtRawAddress((ulong) addr));

                ret = DisassembleBytes(theDll.is32Bit, buff.ToArray());

                if (ret.All(i => !i.Error) && ret.Any(i => i.Mnemonic == ud_mnemonic_code.UD_Iint3))
                    con = false;

                if (peek && buff.Count > 50)
                    con = false;
                else if (buff.Count > 1000)
                    con = false; //Sanity breakout.

                addr++;
            }

            return ret /*.Where(i => !i.Error).ToList()*/;
        }

        public static List<Instruction> DisassembleBytes(bool is32Bit, byte[] bytes)
        {
            return new List<Instruction>(new Disassembler(bytes, is32Bit ? ArchitectureMode.x86_32 : ArchitectureMode.x86_64, 0, true).Disassemble());
        }

        public static ulong GetJumpTarget(Instruction insn, ulong start)
        {
            var opr = insn.Operands[0];

            var mode = GetOprMode(insn);

            var num = UInt64.MaxValue >> 64 - mode;
            return opr.Size switch
            {
                8 => (start + (ulong) opr.LvalSByte & num),
                16 => (start + (ulong) opr.LvalSWord & num),
                32 => (start + (ulong) opr.LvalSDWord & num),
                64 => (start + (ulong) opr.LvalSQWord & num),
                _ => throw new InvalidOperationException($"invalid relative offset size {opr.Size}.")
            };
        }

        public static byte GetOprMode(Instruction instruction)
        {
            return (byte) oprMode.GetValue(instruction);
        }

        public static ulong GetImmediateValue(Instruction insn, Operand op)
        {
            ulong num;
            if (op.Opcode == ud_operand_code.OP_sI && op.Size != GetOprMode(insn))
            {
                if (op.Size == 8)
                {
                    num = (ulong) op.LvalSByte;
                }
                else
                {
                    if (op.Size != 32)
                        throw new InvalidOperationException("Operand size must be 32");
                    num = (ulong) op.LvalSDWord;
                }

                if (GetOprMode(insn) < 64)
                    num &= (ulong) ((1L << GetOprMode(insn)) - 1L);
            }
            else
            {
                switch (op.Size)
                {
                    case 8:
                        num = op.LvalByte;
                        break;
                    case 16:
                        num = op.LvalUWord;
                        break;
                    case 32:
                        num = op.LvalUDWord;
                        break;
                    case 64:
                        num = op.LvalUQWord;
                        break;
                    default:
                        throw new InvalidOperationException($"Invalid size for operand: {op.Size}");
                }
            }

            return num;
        }

        public static FieldInfo oprMode = typeof(Instruction).GetField("opr_mode", BindingFlags.Instance | BindingFlags.NonPublic)!;

        public static ulong GetOffsetFromMemoryAccess(Instruction insn, Operand op)
        {
            var num1 = (ulong) GetOperandMemoryOffset(op);

            if (num1 == 0) return 0;

            return num1 + insn.PC;
        }

        public static int GetOperandMemoryOffset(Operand op)
        {
            if (op.Type != ud_type.UD_OP_MEM) return 0;
            var num1 = op.Offset switch
            {
                8 => op.LvalSByte,
                16 => op.LvalSWord,
                32 => op.LvalSDWord,
                _ => 0
            };
            return num1;
        }

        public static int GetPointerSizeBytes()
        {
            return LibCpp2IlMain.Binary!.is32Bit ? 4 : 8;
        }

        public static object GetNumericConstant(ulong addr, TypeReference type)
        {
            var rawAddr = LibCpp2IlMain.Binary!.MapVirtualAddressToRaw(addr);
            var bytes = LibCpp2IlMain.Binary.ReadByteArrayAtRawAddress(rawAddr, (int) GetSizeOfObject(type));

            if (type == Int32Reference)
                return BitConverter.ToInt32(bytes);

            if (type == Int64Reference)
                return BitConverter.ToInt64(bytes);

            if (type == SingleReference)
                return BitConverter.ToSingle(bytes);

            if (type == DoubleReference)
                return BitConverter.ToDouble(bytes);

            throw new ArgumentException("Do not know how to get a numeric constant of type " + type);
        }

        public static TypeReference? TryResolveTypeReflectionData(Il2CppTypeReflectionData? typeData) => TryResolveTypeReflectionData(typeData, null);

        public static TypeReference? TryResolveTypeReflectionData(Il2CppTypeReflectionData? typeData, IGenericParameterProvider? owner)
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
                var baseType = SharedState.UnmanagedToManagedTypes[typeData.baseType];

                var genericType = baseType.MakeGenericType(typeData.genericParams.Select(a => TryResolveTypeReflectionData(a, baseType)).ToArray());

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

        public static long[] ReadArrayInitializerForFieldDefinition(FieldDefinition fieldDefinition, AllocatedArray allocatedArray)
        {
            var fieldDef = SharedState.ManagedToUnmanagedFields[fieldDefinition];
            var (dataIndex, _) = LibCpp2IlMain.TheMetadata!.GetFieldDefaultValue(fieldDef.FieldIndex);

            var metadata = LibCpp2IlMain.TheMetadata!;

            var pointer = metadata.GetDefaultValueFromIndex(dataIndex);
            var results = new long[allocatedArray.Size];

            if (pointer <= 0) return results;

            //This should at least work for simple arrays.
            var elementSize = GetSizeOfObject(allocatedArray.ArrayType.ElementType);

            for (var i = 0; i < allocatedArray.Size; i++)
            {
                results[i] = Convert.ToInt64(elementSize switch
                {
                    1 => metadata.ReadClassAtRawAddr<byte>(pointer)!,
                    2 => metadata.ReadClassAtRawAddr<short>(pointer)!,
                    4 => metadata.ReadClassAtRawAddr<int>(pointer)!,
                    8 => metadata.ReadClassAtRawAddr<long>(pointer)!,
                    _ => results[i]
                });
            }

            return results;
        }
    }
}