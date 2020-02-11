using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Cpp2IL.Metadata;
using Cpp2IL.PE;
using Mono.Cecil;
using SharpDisasm;
using SharpDisasm.Udis86;

namespace Cpp2IL
{
    public static class Utils
    {
        private static TypeDefinition StringReference ;
        private static TypeDefinition Int64Reference ;
        private static TypeDefinition SingleReference ;
        private static TypeDefinition Int32Reference ;
        private static TypeDefinition BooleanReference;

        private static Dictionary<string, TypeDefinition> primitiveTypeMappings = new Dictionary<string, TypeDefinition>();

        public static void BuildPrimitiveMappings()
        {
            StringReference = TryLookupTypeDefByName("System.String").Item1;
            Int64Reference = TryLookupTypeDefByName("System.Int64").Item1;
            SingleReference = TryLookupTypeDefByName("System.Single").Item1;
            Int32Reference = TryLookupTypeDefByName("System.Int32").Item1;
            BooleanReference = TryLookupTypeDefByName("System.Boolean").Item1;
            
            primitiveTypeMappings = new Dictionary<string, TypeDefinition>
            {
                {"string", StringReference},
                {"long", Int64Reference},
                {"float", SingleReference},
                {"int", Int32Reference},
                {"bool", BooleanReference},
            };
        }

        public static TypeReference ImportTypeInto(MemberReference importInto, Il2CppType toImport, PE.PE theDll, Il2CppMetadata metadata)
        {
            var moduleDefinition = importInto.Module;
            switch (toImport.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                    return moduleDefinition.ImportReference(typeof(object));
                case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                    return moduleDefinition.ImportReference(typeof(void));
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                    return moduleDefinition.ImportReference(typeof(bool));
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                    return moduleDefinition.ImportReference(typeof(char));
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                    return moduleDefinition.ImportReference(typeof(sbyte));
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                    return moduleDefinition.ImportReference(typeof(byte));
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                    return moduleDefinition.ImportReference(typeof(short));
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                    return moduleDefinition.ImportReference(typeof(ushort));
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                    return moduleDefinition.ImportReference(typeof(int));
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                    return moduleDefinition.ImportReference(typeof(uint));
                case Il2CppTypeEnum.IL2CPP_TYPE_I:
                    return moduleDefinition.ImportReference(typeof(IntPtr));
                case Il2CppTypeEnum.IL2CPP_TYPE_U:
                    return moduleDefinition.ImportReference(typeof(UIntPtr));
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                    return moduleDefinition.ImportReference(typeof(long));
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                    return moduleDefinition.ImportReference(typeof(ulong));
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                    return moduleDefinition.ImportReference(typeof(float));
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                    return moduleDefinition.ImportReference(typeof(double));
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                    return moduleDefinition.ImportReference(typeof(string));
                case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                    return moduleDefinition.ImportReference(typeof(TypedReference));
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                {
                    var typeDefinition = SharedState.TypeDefsByAddress[toImport.data.classIndex];
                    return moduleDefinition.ImportReference(typeDefinition);
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                {
                    var arrayType = theDll.ReadClassAtVirtualAddress<Il2CppArrayType>(toImport.data.array);
                    var oriType = theDll.GetIl2CppType(arrayType.etype);
                    return new ArrayType(ImportTypeInto(importInto, oriType, theDll, metadata), arrayType.rank);
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                {
                    var genericClass =
                        theDll.ReadClassAtVirtualAddress<Il2CppGenericClass>(toImport.data.generic_class);
                    var typeDefinition = SharedState.TypeDefsByAddress[genericClass.typeDefinitionIndex];
                    var genericInstanceType = new GenericInstanceType(moduleDefinition.ImportReference(typeDefinition));
                    var genericInst =
                        theDll.ReadClassAtVirtualAddress<Il2CppGenericInst>(genericClass.context.class_inst);
                    var pointers = theDll.GetPointers(genericInst.type_argv, (long) genericInst.type_argc);
                    foreach (var pointer in pointers)
                    {
                        var oriType = theDll.GetIl2CppType(pointer);
                        genericInstanceType.GenericArguments.Add(ImportTypeInto(importInto, oriType, theDll,
                            metadata));
                    }

                    return genericInstanceType;
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                {
                    var oriType = theDll.GetIl2CppType(toImport.data.type);
                    return new ArrayType(ImportTypeInto(importInto, oriType, theDll, metadata));
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                {
                    if (SharedState.GenericParamsByIndex.TryGetValue(toImport.data.genericParameterIndex,
                        out var genericParameter))
                    {
                        return genericParameter;
                    }

                    var param = metadata.genericParameters[toImport.data.genericParameterIndex];
                    var genericName = metadata.GetStringFromIndex(param.nameIndex);
                    if (importInto is MethodDefinition methodDefinition)
                    {
                        genericParameter = new GenericParameter(genericName, methodDefinition.DeclaringType);
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
                    var oriType = theDll.GetIl2CppType(toImport.data.type);
                    return new PointerType(ImportTypeInto(importInto, oriType, theDll, metadata));
                }

                default:
                    return moduleDefinition.ImportReference(typeof(object));
            }
        }

        internal static object GetDefaultValue(int dataIndex, int typeIndex, Il2CppMetadata metadata, PE.PE theDll)
        {
            var pointer = metadata.GetDefaultValueFromIndex(dataIndex);
            if (pointer > 0)
            {
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
                }
            }

            return null;
        }

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

        internal static string GetTypeName(Il2CppMetadata metadata, PE.PE cppAssembly, Il2CppType type, bool fullName = false)
        {
            string ret;
            switch (type.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                {
                    var typeDef = metadata.typeDefs[type.data.classIndex];
                    ret = string.Empty;
                    if (fullName)
                    {
                        ret = metadata.GetStringFromIndex(typeDef.namespaceIndex);
                        if (ret != String.Empty)
                        {
                            ret += ".";
                        }
                    }

                    ret += GetTypeName(metadata, cppAssembly, typeDef);
                    break;
                }
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                {
                    var genericClass = cppAssembly.ReadClassAtVirtualAddress<Il2CppGenericClass>(type.data.generic_class);
                    var typeDef = metadata.typeDefs[genericClass.typeDefinitionIndex];
                    ret = metadata.GetStringFromIndex(typeDef.nameIndex);
                    var genericInst = cppAssembly.ReadClassAtVirtualAddress<Il2CppGenericInst>(genericClass.context.class_inst);
                    ret = ret.Replace($"`{genericInst.type_argc}", "");
                    ret += GetGenericTypeParams(metadata, cppAssembly, genericInst);
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
                    var oriType = cppAssembly.GetIl2CppType(arrayType.etype);
                    ret = $"{GetTypeName(metadata, cppAssembly, oriType)}[{new string(',', arrayType.rank - 1)}]";
                    break;
                }
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                {
                    var oriType = cppAssembly.GetIl2CppType(type.data.type);
                    ret = $"{GetTypeName(metadata, cppAssembly, oriType)}[]";
                    break;
                }
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                {
                    var oriType = cppAssembly.GetIl2CppType(type.data.type);
                    ret = $"{GetTypeName(metadata, cppAssembly, oriType)}*";
                    break;
                }
                default:
                    ret = TypeString[(int) type.type];
                    break;
            }

            return ret;
        }

        internal static string GetTypeName(Il2CppMetadata metadata, PE.PE cppAssembly, Il2CppTypeDefinition typeDef)
        {
            var ret = String.Empty;
            if (typeDef.declaringTypeIndex != -1)
            {
                ret += GetTypeName(metadata, cppAssembly, cppAssembly.types[typeDef.declaringTypeIndex]) + ".";
            }

            ret += metadata.GetStringFromIndex(typeDef.nameIndex);
            var names = new List<string>();
            if (typeDef.genericContainerIndex >= 0)
            {
                var genericContainer = metadata.genericContainers[typeDef.genericContainerIndex];
                for (int i = 0; i < genericContainer.type_argc; i++)
                {
                    var genericParameterIndex = genericContainer.genericParameterStart + i;
                    var param = metadata.genericParameters[genericParameterIndex];
                    names.Add(metadata.GetStringFromIndex(param.nameIndex));
                }

                ret = ret.Replace($"`{genericContainer.type_argc}", "");
                ret += $"<{String.Join(", ", names)}>";
            }

            return ret;
        }

        internal static string GetGenericTypeParams(Il2CppMetadata metadata, PE.PE cppAssembly, Il2CppGenericInst genericInst)
        {
            var typeNames = new List<string>();
            var pointers = cppAssembly.ReadClassArrayAtVirtualAddress<ulong>(genericInst.type_argv, (long) genericInst.type_argc);
            for (uint i = 0; i < genericInst.type_argc; ++i)
            {
                var oriType = cppAssembly.GetIl2CppType(pointers[i]);
                typeNames.Add(GetTypeName(metadata, cppAssembly, oriType));
            }

            return $"<{String.Join(", ", typeNames)}>";
        }

        private static FieldInfo oprMode = typeof(Instruction).GetField("opr_mode", BindingFlags.Instance | BindingFlags.NonPublic);

        public static byte GetOprMode(Instruction instruction)
        {
            return (byte) oprMode.GetValue(instruction); //Reflection!
        }

        public static ulong GetJumpTarget(Instruction insn, ulong start)
        {
            var opr = insn.Operands[0];

            var mode = GetOprMode(insn);

            //Console.WriteLine(mode + " " + mode.GetType());

            var num = ulong.MaxValue >> 64 - mode;
            return opr.Size switch
            {
                8 => (start + (ulong) opr.LvalSByte & num),
                16 => (start + (ulong) opr.LvalSWord & num),
                32 => (start + (ulong) opr.LvalSDWord & num),
                64 => (start + (ulong) opr.LvalSQWord & num),
                _ => throw new InvalidOperationException($"invalid relative offset size {opr.Size}.")
            };
        }

        public static List<Instruction> DisassembleBytes(byte[] bytes)
        {
            return new List<Instruction>(new Disassembler(bytes, ArchitectureMode.x86_64, 0, true).Disassemble());
        }

        public static bool CheckForNullCheckAtIndex(ulong offsetInRam, PE.PE cppAssembly, List<Instruction> instructions, int idx, KeyFunctionAddresses kfe)
        {
            if (instructions.Count - idx < 2) return false;

            var insn = instructions[idx];

            //Check this is a valid TEST RCX RCX
            if (insn.Mnemonic != ud_mnemonic_code.UD_Itest || insn.Operands.Length != 2 || insn.Operands[0].Base != insn.Operands[1].Base)
                return false;

            //Get the following instruction and verify it's a JZ
            var jump = instructions[idx + 1];
            if (jump.Mnemonic != ud_mnemonic_code.UD_Ijz) return false;

            //Get the target of the JZ
            var addrOfCall = GetJumpTarget(jump, offsetInRam + jump.PC);

            //Disassemble 5 bytes at that destination (it should be a call)
            var bytes = cppAssembly.raw.SubArray((int) cppAssembly.MapVirtualAddressToRaw(addrOfCall), 5);
            var callInstruction = DisassembleBytes(bytes).First();

            //Make sure it *is* a call
            if (callInstruction.Mnemonic != ud_mnemonic_code.UD_Icall) return false;

            //Get where that call points to
            var addr = GetJumpTarget(callInstruction, addrOfCall + (ulong) bytes.Length);

            //If it's the bailout then the original check was a il2cpp-generated null check
            return addr == kfe.AddrBailOutFunction;
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

            ulong callAddr;
            if (!alternativePattern.SequenceEqual(actualPattern)) return 0;

            try
            {
                callAddr = GetJumpTarget(instructionsInRange[2], offsetInRam + instructionsInRange[2].PC);
                return callAddr == kfe.AddrInitFunction ? 3 : 0;
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
                return callAddr == kfe.AddrInitStaticFunction ? 4 : 0;
            }
            else
            {
                if (instructions.Count - idx < 7) return 0;

                instructionsInRange = instructions.GetRange(idx, 6);
                actualPattern = instructionsInRange.Select(i => i.Mnemonic).ToArray();

                if (!alternativePattern.SequenceEqual(actualPattern) && !thirdPattern.SequenceEqual(actualPattern)) return 0;

                var callAddr = GetJumpTarget(instructionsInRange[5], offsetInRam + instructionsInRange[5].PC);

                //If this is true then we have an il2cpp-generated initialization call.
                return callAddr == kfe.AddrInitStaticFunction ? 5 : 0;
            }
        }

        public static int GetOperandMemoryOffset(Operand op)
        {
            if (op.Type != ud_type.UD_OP_MEM) return 0;
            var num1 = op.Offset switch
            {
                8 => (int) op.LvalSByte,
                16 => (int) op.LvalSWord,
                32 => op.LvalSDWord,
                _ => 0
            };
            return num1;
        }

        public static ulong GetOffsetFromMemoryAccess(Instruction insn, Operand op)
        {
            var num1 = (ulong) GetOperandMemoryOffset(op);

            if (num1 == 0) return 0;

            return num1 + insn.PC;
        }

        public static Tuple<TypeDefinition?, string[]> TryLookupTypeDefByName(string name)
        {
            if (name == null) return new Tuple<TypeDefinition, string[]>(null, new string[0]);

            if (primitiveTypeMappings.ContainsKey(name))
                return new Tuple<TypeDefinition, string[]>(primitiveTypeMappings[name], new string[0]);

            var definedType = SharedState.AllTypeDefinitions.Find(t => string.Equals(t.FullName, name, StringComparison.OrdinalIgnoreCase));

            //Generics are dumb.
            var genericParams = new string[0];
            if (definedType == null && name.Contains("<"))
            {
                //Replace < > with the number of generic params after a ` 
                var origName = name;
                genericParams = name.Substring(name.IndexOf("<", StringComparison.Ordinal) + 1).TrimEnd('>').Split(',');
                name = name.Substring(0, name.IndexOf("<", StringComparison.Ordinal));
                if (!name.Contains("`"))
                    name = name + "`" + (origName.Count(c => c == ',') + 1);

                definedType = SharedState.AllTypeDefinitions.Find(t => t.FullName == name);
            }

            if (definedType != null) return new Tuple<TypeDefinition, string[]>(definedType, genericParams);

            //It's possible they didn't specify a `System.` prefix
            var searchString = $"System.{name}";
            definedType = SharedState.AllTypeDefinitions.Find(t => string.Equals(t.FullName, searchString, StringComparison.OrdinalIgnoreCase));

            if (definedType != null) return new Tuple<TypeDefinition, string[]>(definedType, genericParams);

            //Still not got one? Ok, is there only one match for non FQN?
            var matches = SharedState.AllTypeDefinitions.Where(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 1)
                definedType = matches.First();

            if (definedType != null || !name.Contains(".")) return new Tuple<TypeDefinition, string[]>(definedType, genericParams);

            //Try subclasses
            searchString = name.Replace(".", "/");
            matches = SharedState.AllTypeDefinitions.Where(t => t.FullName.EndsWith(searchString)).ToList();
            if (matches.Count == 1)
                definedType = matches.First();


            return new Tuple<TypeDefinition, string[]>(definedType, genericParams);
        }

        public static TypeReference MakeGenericType(this TypeReference self, params TypeReference[] arguments)
        {
            if (self.GenericParameters.Count != arguments.Length)
                throw new ArgumentException();

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
                return condition.Replace("== true", "== false");
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

        public static bool IsAssignableFrom(this TypeReference reference, TypeReference other)
        {
            if(other is TypeDefinition otherDef)
                return reference.FullName == otherDef.FullName || otherDef.BaseType != null && reference.IsAssignableFrom(TryLookupTypeDefByName(otherDef.BaseType.FullName).Item1) || otherDef.Interfaces.Any(i => reference.IsAssignableFrom(i.InterfaceType));
            
            return reference.FullName == other.FullName; //Simple check
        }
    }
}