using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using AudicaShredder.Metadata;
using AudicaShredder.PE;
using Microsoft.Win32;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SharpDisasm;
using SharpDisasm.Udis86;
using EventAttributes = Mono.Cecil.EventAttributes;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using Instruction = SharpDisasm.Instruction;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace AudicaShredder
{
    internal class Program
    {
        public static float MetadataVersion = 24f;

        private static List<AssemblyDefinition> Assemblies = new List<AssemblyDefinition>();
        private static Dictionary<long, TypeDefinition> typeDefsByAddress = new Dictionary<long, TypeDefinition>();
        private static Dictionary<long, MethodDefinition> methodsByIndex = new Dictionary<long, MethodDefinition>();
        private static Dictionary<ulong, MethodDefinition> methodsByAddress = new Dictionary<ulong, MethodDefinition>();
        private static Dictionary<long, GenericParameter> genericParamsByIndex = new Dictionary<long, GenericParameter>();

        public static void PrintUsage()
        {
            Console.WriteLine("Usage: AudicaShredder <path to audica folder>");
        }

        public static void Main(string[] args)
        {
            Console.WriteLine("===AudicaShredder by Samboy063===");
            var loc = Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Steam App 1020340", "InstallLocation", null) as string;

            if (args.Length != 1 && loc == null)
            {
                PrintUsage();
                return;
            }

            var baseGamePath = args.Length > 0 ? args[0] : loc;

            Console.WriteLine("Using path: " + baseGamePath);

            if (!Directory.Exists(baseGamePath))
            {
                Console.WriteLine("Specified path does not exist: " + baseGamePath);
                PrintUsage();
                return;
            }

            var assemblyPath = Path.Combine(baseGamePath, "GameAssembly.dll");
            var unityPlayerPath = Path.Combine(baseGamePath, "Audica.exe");
            var metadataPath = Path.Combine(baseGamePath, "Audica_Data", "il2cpp_data", "Metadata", "global-metadata.dat");

            if (!File.Exists(assemblyPath) || !File.Exists(unityPlayerPath) || !File.Exists(metadataPath))
            {
                Console.WriteLine("Invalid path specified. Failed to find one of the following:\n" +
                                  $"\t{assemblyPath}\n" +
                                  $"\t{unityPlayerPath}\n" +
                                  $"\t{metadataPath}\n");
                PrintUsage();
                return;
            }

            var unityVer = FileVersionInfo.GetVersionInfo(unityPlayerPath).FileVersion;
            var split = unityVer.Split(new[] {'.'}, StringSplitOptions.RemoveEmptyEntries);
            var unityVerUseful = split.SubArray(0, 2).Select(int.Parse).ToArray();

            Console.WriteLine("This version of Audica is built on unity " + string.Join(".", unityVerUseful));

            Console.WriteLine("Reading metadata...");
            var metadata = Il2CppMetadata.ReadFrom(metadataPath, unityVerUseful);

            var PEBytes = File.ReadAllBytes(assemblyPath);

            var theDll = new PE.PE(new MemoryStream(PEBytes), metadata.maxMetadataUsages);
            if (!theDll.PlusSearch(metadata.methodDefs.Count(x => x.methodIndex >= 0), metadata.typeDefs.Length))
            {
                Console.WriteLine("Initialize failed. Aborting.");
                return;
            }

            //Dump DLLs

            #region Assembly Generation

            var resolver = new RegistryAssemblyResolver();
            var moduleParams = new ModuleParameters
            {
                Kind = ModuleKind.Dll,
                AssemblyResolver = resolver
            };


            Console.WriteLine("Building assemblies...");
            Console.WriteLine("\tPass 1: Creating types...");

            var infoTxt = new StringBuilder();
            var addressMapText = new StringBuilder();
            var methodBytes = new Dictionary<string, Dictionary<Tuple<string, int>, byte[]>>();

            foreach (var imageDefinition in metadata.imageDefs)
            {
                var asmName = new AssemblyNameDefinition(metadata.GetStringFromIndex(imageDefinition.nameIndex).Replace(".dll", ""), new Version("0.0.0.0"));
                Console.Write($"\t\t{asmName.Name}...");

                var assembly = AssemblyDefinition.CreateAssembly(asmName, metadata.GetStringFromIndex(imageDefinition.nameIndex), moduleParams);
                resolver.Register(assembly);
                Assemblies.Add(assembly);

                var mainModule = assembly.MainModule;
                mainModule.Types.Clear();
                var end = imageDefinition.typeStart + imageDefinition.typeCount;

                for (var defNumber = imageDefinition.typeStart; defNumber < end; defNumber++)
                {
                    var type = metadata.typeDefs[defNumber];
                    var ns = metadata.GetStringFromIndex(type.namespaceIndex);
                    var name = metadata.GetStringFromIndex(type.nameIndex);

                    TypeDefinition definition;
                    if (type.declaringTypeIndex != -1)
                    {
                        definition = typeDefsByAddress[defNumber];
                    }
                    else
                    {
                        definition = new TypeDefinition(ns, name, (TypeAttributes) type.flags);
                        mainModule.Types.Add(definition);
                        typeDefsByAddress.Add(defNumber, definition);
                    }

                    for (int nestedNumber = 0; nestedNumber < type.nested_type_count; nestedNumber++)
                    {
                        var nestedIndex = metadata.nestedTypeIndices[type.nestedTypesStart + nestedNumber];
                        var nested = metadata.typeDefs[nestedIndex];
                        var nestedDef = new TypeDefinition(metadata.GetStringFromIndex(nested.namespaceIndex), metadata.GetStringFromIndex(nested.nameIndex), (TypeAttributes) nested.flags);

                        definition.NestedTypes.Add(nestedDef);
                        typeDefsByAddress.Add(nestedIndex, nestedDef);
                    }
                }

                Console.WriteLine("OK");
            }

            Console.WriteLine("\tPass 2: Setting parents and handling inheritance...");
            for (var typeIndex = 0; typeIndex < metadata.typeDefs.Length; typeIndex++)
            {
                var type = metadata.typeDefs[typeIndex];
                var definition = typeDefsByAddress[typeIndex];

                if (type.parentIndex >= 0)
                {
                    var parent = theDll.types[type.parentIndex];
                    var parentRef = GetTypeReference(definition, parent, theDll, metadata);
                    definition.BaseType = parentRef;
                }

                for (var i = 0; i < type.interfaces_count; i++)
                {
                    var interfaceType = theDll.types[metadata.interfaceIndices[type.interfacesStart + i]];
                    var interfaceTypeRef = GetTypeReference(definition, interfaceType, theDll, metadata);
                    definition.Interfaces.Add(new InterfaceImplementation(interfaceTypeRef));
                }
            }

            Console.WriteLine("\tPass 3: Handling Fields, methods, and properties (THIS MAY TAKE A WHILE)...");
            for (var imageIndex = 0; imageIndex < metadata.imageDefs.Length; imageIndex++)
            {
                Console.WriteLine($"\t\tProcessing DLL {imageIndex + 1} of {metadata.imageDefs.Length}...");
                var imageDef = metadata.imageDefs[imageIndex];
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (var index = imageDef.typeStart; index < typeEnd; index++)
                {
                    Console.WriteLine($"\t\t\tProcessing Type {index + 1 - imageDef.typeStart} of {imageDef.typeCount}...");
                    var typeDef = metadata.typeDefs[index];
                    var typeDefinition = typeDefsByAddress[index];

                    if (Assemblies[imageIndex].Name.Name == "Assembly-CSharp" && typeDefinition.Namespace.Length < 2)
                        infoTxt.Append($"\n\nType: {typeDefinition.Name}:");

                    //field
                    var fieldEnd = typeDef.fieldStart + typeDef.field_count;
                    for (var i = typeDef.fieldStart; i < fieldEnd; ++i)
                    {
                        var fieldDef = metadata.fieldDefs[i];
                        var fieldType = theDll.types[fieldDef.typeIndex];
                        var fieldName = metadata.GetStringFromIndex(fieldDef.nameIndex);
                        var fieldTypeRef = GetTypeReference(typeDefinition, fieldType, theDll, metadata);

                        var fieldDefinition = new FieldDefinition(fieldName, (FieldAttributes) fieldType.attrs, fieldTypeRef);
                        typeDefinition.Fields.Add(fieldDefinition);
                        //fieldDefault
                        if (fieldDefinition.HasDefault)
                        {
                            var fieldDefault = metadata.GetFieldDefaultValueFromIndex(i);
                            if (fieldDefault != null && fieldDefault.dataIndex != -1)
                            {
                                fieldDefinition.Constant = GetDefaultValue(fieldDefault.dataIndex, fieldDefault.typeIndex, metadata, theDll);
                            }
                        }

                        if (Assemblies[imageIndex].Name.Name == "Assembly-CSharp" && typeDefinition.Namespace.Length < 2)
                        {
                            infoTxt.Append($"\n\tField: {fieldName}\n")
                                .Append($"\t\tType: {(fieldTypeRef.Namespace == "" ? "<None>" : fieldTypeRef.Namespace)}.{fieldTypeRef.Name}\n")
                                .Append($"\t\tDefault Value: {fieldDefinition.Constant}");
                        }
                    }

                    //method
                    var methodEnd = typeDef.methodStart + typeDef.method_count;
                    var typeMethods = new Dictionary<Tuple<string, int>, byte[]>();
                    for (var i = typeDef.methodStart; i < methodEnd; ++i)
                    {
                        var methodDef = metadata.methodDefs[i];
                        var methodReturnType = theDll.types[methodDef.returnType];
                        var methodName = metadata.GetStringFromIndex(methodDef.nameIndex);
                        var methodDefinition = new MethodDefinition(methodName, (MethodAttributes) methodDef.flags, typeDefinition.Module.ImportReference(typeof(void)));

                        var offsetInRam = theDll.GetMethodPointer(methodDef.methodIndex, i, imageIndex, methodDef.token);

                        if (Assemblies[imageIndex].Name.Name == "Assembly-CSharp" && typeDefinition.Namespace.Length < 2)
                        {
                            long offsetInFile = offsetInRam == 0 ? 0 : theDll.MapVirtualAddressToRaw(offsetInRam);
                            infoTxt.Append($"\n\tMethod: {methodName}:\n")
                                .Append($"\t\tFile Offset 0x{offsetInFile.ToString("X8")}\n")
                                .Append($"\t\tRam Offset 0x{offsetInRam.ToString("x8")}\n");

                            var bytes = new List<byte>();
                            var offset = offsetInFile;
                            while (true)
                            {
                                var b = PEBytes[offset];
                                if (b == 0xCC) break;
                                bytes.Add(b);
                                offset++;
                            }

                            infoTxt.Append($"\t\tMethod Length: {bytes.Count} bytes\n");

                            typeMethods[new Tuple<string, int>(methodName, i)] = bytes.ToArray();
                        }

                        typeDefinition.Methods.Add(methodDefinition);
                        methodDefinition.ReturnType = GetTypeReference(methodDefinition, methodReturnType, theDll, metadata);
                        if (methodDefinition.HasBody && typeDefinition.BaseType?.FullName != "System.MulticastDelegate")
                        {
                            var ilprocessor = methodDefinition.Body.GetILProcessor();
                            ilprocessor.Append(ilprocessor.Create(OpCodes.Nop));
                        }

                        methodsByIndex.Add(i, methodDefinition);
                        //method parameter
                        for (var j = 0; j < methodDef.parameterCount; ++j)
                        {
                            var parameterDef = metadata.parameterDefs[methodDef.parameterStart + j];
                            var parameterName = metadata.GetStringFromIndex(parameterDef.nameIndex);
                            var parameterType = theDll.types[parameterDef.typeIndex];
                            var parameterTypeRef = GetTypeReference(methodDefinition, parameterType, theDll, metadata);
                            var parameterDefinition = new ParameterDefinition(parameterName, (ParameterAttributes) parameterType.attrs, parameterTypeRef);
                            methodDefinition.Parameters.Add(parameterDefinition);
                            //ParameterDefault
                            if (parameterDefinition.HasDefault)
                            {
                                var parameterDefault = metadata.GetParameterDefaultValueFromIndex(methodDef.parameterStart + j);
                                if (parameterDefault != null && parameterDefault.dataIndex != -1)
                                {
                                    parameterDefinition.Constant = GetDefaultValue(parameterDefault.dataIndex, parameterDefault.typeIndex, metadata, theDll);
                                }
                            }

                            if (Assemblies[imageIndex].Name.Name == "Assembly-CSharp" && typeDefinition.Namespace.Length < 2)
                            {
                                infoTxt.Append($"\n\t\tParameter {j}:\n")
                                    .Append($"\t\t\tName: {parameterName}\n")
                                    .Append($"\t\t\tType: {(parameterTypeRef.Namespace == "" ? "<None>" : parameterTypeRef.Namespace)}.{parameterTypeRef.Name}\n")
                                    .Append($"\t\t\tDefault Value: {parameterDefinition.Constant}");
                            }
                        }

                        if (methodDef.genericContainerIndex >= 0)
                        {
                            var genericContainer = metadata.genericContainers[methodDef.genericContainerIndex];
                            if (genericContainer.type_argc > methodDefinition.GenericParameters.Count)
                            {
                                for (int j = 0; j < genericContainer.type_argc; j++)
                                {
                                    var genericParameterIndex = genericContainer.genericParameterStart + j;
                                    var param = metadata.genericParameters[genericParameterIndex];
                                    var genericName = metadata.GetStringFromIndex(param.nameIndex);
                                    if (!genericParamsByIndex.TryGetValue(genericParameterIndex, out var genericParameter))
                                    {
                                        genericParameter = new GenericParameter(genericName, methodDefinition);
                                        methodDefinition.GenericParameters.Add(genericParameter);
                                        genericParamsByIndex.Add(genericParameterIndex, genericParameter);
                                    }
                                    else
                                    {
                                        if (!methodDefinition.GenericParameters.Contains(genericParameter))
                                        {
                                            methodDefinition.GenericParameters.Add(genericParameter);
                                        }
                                    }
                                }
                            }
                        }

                        methodsByAddress[offsetInRam] = methodDefinition;
                        addressMapText.Append($"0x{offsetInRam:X} => {methodDefinition.FullName}\n");
                    }

                    if (Assemblies[imageIndex].Name.Name == "Assembly-CSharp" && typeDefinition.Namespace.Length < 2)
                        methodBytes[typeDefinition.Name] = typeMethods;

                    //property
                    var propertyEnd = typeDef.propertyStart + typeDef.property_count;
                    for (var i = typeDef.propertyStart; i < propertyEnd; ++i)
                    {
                        var propertyDef = metadata.propertyDefs[i];
                        var propertyName = metadata.GetStringFromIndex(propertyDef.nameIndex);
                        TypeReference propertyType = null;
                        MethodDefinition GetMethod = null;
                        MethodDefinition SetMethod = null;
                        if (propertyDef.get >= 0)
                        {
                            GetMethod = methodsByIndex[typeDef.methodStart + propertyDef.get];
                            propertyType = GetMethod.ReturnType;
                        }

                        if (propertyDef.set >= 0)
                        {
                            SetMethod = methodsByIndex[typeDef.methodStart + propertyDef.set];
                            if (propertyType == null)
                                propertyType = SetMethod.Parameters[0].ParameterType;
                        }

                        var propertyDefinition = new PropertyDefinition(propertyName, (PropertyAttributes) propertyDef.attrs, propertyType)
                        {
                            GetMethod = GetMethod,
                            SetMethod = SetMethod
                        };
                        typeDefinition.Properties.Add(propertyDefinition);
                    }

                    //event
                    var eventEnd = typeDef.eventStart + typeDef.event_count;
                    for (var i = typeDef.eventStart; i < eventEnd; ++i)
                    {
                        var eventDef = metadata.eventDefs[i];
                        var eventName = metadata.GetStringFromIndex(eventDef.nameIndex);
                        var eventType = theDll.types[eventDef.typeIndex];
                        var eventTypeRef = GetTypeReference(typeDefinition, eventType, theDll, metadata);
                        var eventDefinition = new EventDefinition(eventName, (EventAttributes) eventType.attrs, eventTypeRef);
                        if (eventDef.add >= 0)
                            eventDefinition.AddMethod = methodsByIndex[typeDef.methodStart + eventDef.add];
                        if (eventDef.remove >= 0)
                            eventDefinition.RemoveMethod = methodsByIndex[typeDef.methodStart + eventDef.remove];
                        if (eventDef.raise >= 0)
                            eventDefinition.InvokeMethod = methodsByIndex[typeDef.methodStart + eventDef.raise];
                        typeDefinition.Events.Add(eventDefinition);
                    }

                    if (typeDef.genericContainerIndex >= 0)
                    {
                        var genericContainer = metadata.genericContainers[typeDef.genericContainerIndex];
                        if (genericContainer.type_argc > typeDefinition.GenericParameters.Count)
                        {
                            for (int i = 0; i < genericContainer.type_argc; i++)
                            {
                                var genericParameterIndex = genericContainer.genericParameterStart + i;
                                var param = metadata.genericParameters[genericParameterIndex];
                                var genericName = metadata.GetStringFromIndex(param.nameIndex);
                                if (!genericParamsByIndex.TryGetValue(genericParameterIndex, out var genericParameter))
                                {
                                    genericParameter = new GenericParameter(genericName, typeDefinition);
                                    typeDefinition.GenericParameters.Add(genericParameter);
                                    genericParamsByIndex.Add(genericParameterIndex, genericParameter);
                                }
                                else
                                {
                                    if (!typeDefinition.GenericParameters.Contains(genericParameter))
                                    {
                                        typeDefinition.GenericParameters.Add(genericParameter);
                                    }
                                }
                            }
                        }
                    }
                }
            }

            Console.WriteLine("\tPass 4: Handling SerializeFields...");
            //Add serializefield to monobehaviors
            var engine = Assemblies.Find(x => x.MainModule.Types.Any(t => t.Namespace == "UnityEngine" && t.Name == "SerializeField"));
            if (engine != null)
            {
                var serializeField = engine.MainModule.Types.First(x => x.Name == "SerializeField").Methods.First();
                foreach (var imageDef in metadata.imageDefs)
                {
                    var typeEnd = imageDef.typeStart + imageDef.typeCount;
                    for (int index = imageDef.typeStart; index < typeEnd; index++)
                    {
                        var typeDef = metadata.typeDefs[index];
                        var typeDefinition = typeDefsByAddress[index];
                        //field
                        var fieldEnd = typeDef.fieldStart + typeDef.field_count;
                        for (var i = typeDef.fieldStart; i < fieldEnd; ++i)
                        {
                            var fieldDef = metadata.fieldDefs[i];
                            var fieldName = metadata.GetStringFromIndex(fieldDef.nameIndex);
                            var fieldDefinition = typeDefinition.Fields.First(x => x.Name == fieldName);
                            //fieldAttribute
                            var attributeIndex = metadata.GetCustomAttributeIndex(imageDef, fieldDef.customAttributeIndex, fieldDef.token);
                            if (attributeIndex >= 0)
                            {
                                var attributeTypeRange = metadata.attributeTypeRanges[attributeIndex];
                                for (int j = 0; j < attributeTypeRange.count; j++)
                                {
                                    var attributeTypeIndex = metadata.attributeTypes[attributeTypeRange.start + j];
                                    var attributeType = theDll.types[attributeTypeIndex];
                                    if (attributeType.type == Il2CppTypeEnum.IL2CPP_TYPE_CLASS)
                                    {
                                        var klass = metadata.typeDefs[attributeType.data.klassIndex];
                                        var attributeName = metadata.GetStringFromIndex(klass.nameIndex);
                                        if (attributeName == "SerializeField")
                                        {
                                            var customAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(serializeField));
                                            fieldDefinition.CustomAttributes.Add(customAttribute);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            #endregion

            var outputPath = Path.GetFullPath("audica_shredder_out");
            if (!Directory.Exists(outputPath))
                Directory.CreateDirectory(outputPath);

            var methodOutputDir = Path.Combine(outputPath, "methods");
            if (!Directory.Exists(methodOutputDir))
                Directory.CreateDirectory(methodOutputDir);

            File.WriteAllText(Path.Combine(outputPath, "info.txt"), infoTxt.ToString());
            File.WriteAllText(Path.Combine(outputPath, "function_map.txt"), addressMapText.ToString());

            Console.WriteLine("Saving DLLs to " + outputPath + "...");

            foreach (var assembly in Assemblies)
            {
                var dllPath = Path.Combine(outputPath, assembly.MainModule.Name);

                Console.Write("\t" + assembly.MainModule.Name + "...");
                var start = DateTime.Now;
                assembly.Write(dllPath);
                Console.WriteLine($"OK ({(DateTime.Now - start).TotalMilliseconds} ms)");

                if (assembly.Name.Name == "Assembly-CSharp")
                {
                    Console.WriteLine("Dumping method bytes to " + methodOutputDir);
                    //Write methods

                    Disassembler.Translator.IncludeAddress = true;
                    Disassembler.Translator.IncludeBinary = true;

                    var imageIndex = Assemblies.IndexOf(assembly);
                    var allUsedMnemonics = new List<ud_mnemonic_code>();

                    foreach (var type in methodBytes)
                    {
                        Console.WriteLine("\t-Dumping methods in type: " + type.Key);
                        try
                        {
                            var filename = Path.Combine(methodOutputDir, type.Key.Replace("<", "_").Replace(">", "_") + ".txt");
                            var typeDump = new StringBuilder("Type: " + type.Key + "\n\n");

                            foreach (var method in type.Value)
                            {
                                typeDump.Append($"Method: {method.Key.Item1}: (");

                                var methodDef = metadata.methodDefs[method.Key.Item2];
                                var methodStart = theDll.GetMethodPointer(methodDef.methodIndex, method.Key.Item2, imageIndex, methodDef.token);

                                var disasm = new Disassembler(method.Value, ArchitectureMode.x86_64, 0, true);
                                var instructions = new List<Instruction>(disasm.Disassemble());

                                var distinctMnemonics = new List<ud_mnemonic_code>(instructions.Select(i => i.Mnemonic).Distinct());
                                typeDump.Append($"uses {distinctMnemonics.Count} unique operations)\n");
                                allUsedMnemonics = new List<ud_mnemonic_code>(allUsedMnemonics.Concat(distinctMnemonics).Distinct());

                                foreach (var instruction in instructions)
                                {
                                    typeDump.Append($"\t{instruction}");
                                    if (instruction.Mnemonic == ud_mnemonic_code.UD_Ijmp || instruction.Mnemonic == ud_mnemonic_code.UD_Icall)
                                    {
                                        try
                                        {
                                            //JMP instruction, try find function
                                            var jumpTarget = GetJumpTarget(instruction, methodStart + instruction.PC);
                                            typeDump.Append($" ; jump to 0x{jumpTarget:X}");

                                            var target = methodsByAddress.ContainsKey(jumpTarget) ? methodsByAddress[jumpTarget] : null;

                                            if (target != null)
                                            {
                                                //Console.WriteLine("Found a function call!");
                                                typeDump.Append($" - function {target.FullName}");
                                            }
                                            else
                                            {
                                                //Is this somewhere in this function?
                                                var methodEnd = methodStart + (ulong) instructions.Count;
                                                if (methodStart <= jumpTarget && jumpTarget <= methodEnd)
                                                {
                                                    var pos = jumpTarget - methodStart;
                                                    typeDump.Append($" - offset 0x{pos:X} in this function");
                                                }
                                            }
                                        }
                                        catch (Exception e)
                                        {
                                            typeDump.Append($" ; {e.GetType()} thrown trying to locate JMP target.");
                                        }
                                    }

                                    typeDump.Append("\n");
                                }

                                typeDump.Append("\n");
                            }

                            File.WriteAllText(filename, typeDump.ToString());
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Failed to dump methods for type " + type.Key + " " + e);
                        }
                    }

                    Console.WriteLine("Assembly uses " + allUsedMnemonics.Count + " mnemonics");
                }
            }
        }

        private static ulong GetJumpTarget(Instruction insn, ulong start)
        {
            var opr = insn.Operands[0];

            var mode = insn.GetType().GetField("opr_mode", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(insn); //Reflection!

            //Console.WriteLine(mode + " " + mode.GetType());

            var num = ulong.MaxValue >> 64 - (byte) mode;
            switch (opr.Size)
            {
                case 8:
                    return start + (ulong) opr.LvalSByte & num;
                case 16:
                    return start + (ulong) opr.LvalSWord & num;
                case 32:
                    return start + (ulong) opr.LvalSDWord & num;
                default:
                    throw new InvalidOperationException(string.Format("invalid relative offset size {0}.", opr.Size));
            }
        }


        #region Assembly Generation Helper Functions

        public class RegistryAssemblyResolver : DefaultAssemblyResolver
        {
            public void Register(AssemblyDefinition assembly)
            {
                RegisterAssembly(assembly);
            }
        }

        private static TypeReference GetTypeReference(MemberReference memberReference, Il2CppType il2CppType, PE.PE theDll, Il2CppMetadata metadata)
        {
            var moduleDefinition = memberReference.Module;
            switch (il2CppType.type)
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
                    var typeDefinition = typeDefsByAddress[il2CppType.data.klassIndex];
                    return moduleDefinition.ImportReference(typeDefinition);
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                {
                    var arrayType = theDll.ReadClassAtVirtualAddress<Il2CppArrayType>(il2CppType.data.array);
                    var oriType = theDll.GetIl2CppType(arrayType.etype);
                    return new ArrayType(GetTypeReference(memberReference, oriType, theDll, metadata), arrayType.rank);
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                {
                    var genericClass = theDll.ReadClassAtVirtualAddress<Il2CppGenericClass>(il2CppType.data.generic_class);
                    var typeDefinition = typeDefsByAddress[genericClass.typeDefinitionIndex];
                    var genericInstanceType = new GenericInstanceType(moduleDefinition.ImportReference(typeDefinition));
                    var genericInst = theDll.ReadClassAtVirtualAddress<Il2CppGenericInst>(genericClass.context.class_inst);
                    var pointers = theDll.GetPointers(genericInst.type_argv, (long) genericInst.type_argc);
                    foreach (var pointer in pointers)
                    {
                        var oriType = theDll.GetIl2CppType(pointer);
                        genericInstanceType.GenericArguments.Add(GetTypeReference(memberReference, oriType, theDll, metadata));
                    }

                    return genericInstanceType;
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                {
                    var oriType = theDll.GetIl2CppType(il2CppType.data.type);
                    return new ArrayType(GetTypeReference(memberReference, oriType, theDll, metadata));
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                {
                    if (genericParamsByIndex.TryGetValue(il2CppType.data.genericParameterIndex, out var genericParameter))
                    {
                        return genericParameter;
                    }

                    var param = metadata.genericParameters[il2CppType.data.genericParameterIndex];
                    var genericName = metadata.GetStringFromIndex(param.nameIndex);
                    if (memberReference is MethodDefinition methodDefinition)
                    {
                        genericParameter = new GenericParameter(genericName, methodDefinition.DeclaringType);
                        methodDefinition.DeclaringType.GenericParameters.Add(genericParameter);
                        genericParamsByIndex.Add(il2CppType.data.genericParameterIndex, genericParameter);
                        return genericParameter;
                    }

                    var typeDefinition = (TypeDefinition) memberReference;
                    genericParameter = new GenericParameter(genericName, typeDefinition);
                    typeDefinition.GenericParameters.Add(genericParameter);
                    genericParamsByIndex.Add(il2CppType.data.genericParameterIndex, genericParameter);
                    return genericParameter;
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                {
                    if (genericParamsByIndex.TryGetValue(il2CppType.data.genericParameterIndex, out var genericParameter))
                    {
                        return genericParameter;
                    }

                    var methodDefinition = (MethodDefinition) memberReference;
                    var param = metadata.genericParameters[il2CppType.data.genericParameterIndex];
                    var genericName = metadata.GetStringFromIndex(param.nameIndex);
                    genericParameter = new GenericParameter(genericName, methodDefinition);
                    methodDefinition.GenericParameters.Add(genericParameter);
                    genericParamsByIndex.Add(il2CppType.data.genericParameterIndex, genericParameter);
                    return genericParameter;
                }

                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                {
                    var oriType = theDll.GetIl2CppType(il2CppType.data.type);
                    return new PointerType(GetTypeReference(memberReference, oriType, theDll, metadata));
                }

                default:
                    return moduleDefinition.ImportReference(typeof(object));
            }
        }

        private static object GetDefaultValue(int dataIndex, int typeIndex, Il2CppMetadata metadata, PE.PE theDll)
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

        #endregion
    }
}