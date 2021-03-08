using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cpp2IL.Analysis;
using LibCpp2IL;
using LibCpp2IL.Metadata;
using LibCpp2IL.PE;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using CustomAttributeNamedArgument = Mono.Cecil.CustomAttributeNamedArgument;
using EventAttributes = Mono.Cecil.EventAttributes;
using FieldAttributes = Mono.Cecil.FieldAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;
using PropertyAttributes = Mono.Cecil.PropertyAttributes;
using TypeAttributes = Mono.Cecil.TypeAttributes;

namespace Cpp2IL
{
    internal static class AssemblyPopulator
    {
        private const string InjectedNamespaceName = "Cpp2IlInjected";
        private static readonly Dictionary<ModuleDefinition, (MethodDefinition, MethodDefinition, MethodDefinition)> _attributesByModule = new Dictionary<ModuleDefinition, (MethodDefinition, MethodDefinition, MethodDefinition)>();

        internal static bool EmitMetadataFiles;

        public static void ConfigureHierarchy()
        {
            foreach (var typeDefinition in SharedState.AllTypeDefinitions)
            {
                var il2cppTypeDef = SharedState.ManagedToUnmanagedTypes[typeDefinition];

                //Set base type
                if (il2cppTypeDef.RawBaseType is { } parent)
                    typeDefinition.BaseType = Utils.ImportTypeInto(typeDefinition, parent);

                //Set interfaces
                foreach (var interfaceType in il2cppTypeDef.RawInterfaces)
                    typeDefinition.Interfaces.Add(new InterfaceImplementation(Utils.ImportTypeInto(typeDefinition, interfaceType)));
            }
        }

        private static void CreateDefaultConstructor(TypeDefinition typeDefinition)
        {
            var module = typeDefinition.Module;
            var defaultConstructor = new MethodDefinition(
                ".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.ImportReference(Utils.TryLookupTypeDefByName("System.Void").Item1)
            );

            var processor = defaultConstructor.Body.GetILProcessor();
            // processor.Emit(OpCodes.Ldarg_0);
            // processor.Emit(OpCodes.Call, module.ImportReference(Utils.TryLookupTypeDefByName("System.Attribute").Item1.GetConstructors().First()));
            processor.Emit(OpCodes.Ret);

            typeDefinition.Methods.Add(defaultConstructor);
        }

        private static void InjectAttribute(string name, TypeReference stringRef, TypeReference attributeRef, AssemblyDefinition assembly, params string[] fields)
        {
            var attribute = new TypeDefinition(InjectedNamespaceName, name, (TypeAttributes) 0x100001, attributeRef);

            foreach (var field in fields)
                attribute.Fields.Add(new FieldDefinition(field, FieldAttributes.Public, stringRef));

            assembly.MainModule.Types.Add(attribute);
            CreateDefaultConstructor(attribute);
        }

        private static void InjectCustomAttributes(AssemblyDefinition imageDef)
        {
            var stringTypeReference = imageDef.MainModule.ImportReference(Utils.TryLookupTypeDefKnownNotGeneric("System.String"));
            var attributeTypeReference = imageDef.MainModule.ImportReference(Utils.TryLookupTypeDefKnownNotGeneric("System.Attribute"));

            InjectAttribute("AddressAttribute", stringTypeReference, attributeTypeReference, imageDef, "RVA", "Offset", "VA", "Slot");
            InjectAttribute("FieldOffsetAttribute", stringTypeReference, attributeTypeReference, imageDef, "Offset");
            InjectAttribute("AttributeAttribute", stringTypeReference, attributeTypeReference, imageDef, "Name", "RVA", "Offset");
            InjectAttribute("MetadataOffsetAttribute", stringTypeReference, attributeTypeReference, imageDef, "Offset");
            InjectAttribute("TokenAttribute", stringTypeReference, attributeTypeReference, imageDef, "Token");
        }

        public static List<(TypeDefinition type, List<CppMethodData> methods)> ProcessAssemblyTypes(Il2CppMetadata metadata, PE theDll, Il2CppImageDefinition imageDef)
        {
            var firstTypeDefinition = SharedState.TypeDefsByIndex[imageDef.firstTypeIndex];
            var currentAssembly = firstTypeDefinition.Module.Assembly;

            InjectCustomAttributes(currentAssembly);

            //Ensure type directory exists
            if (EmitMetadataFiles)
                Directory.CreateDirectory(Path.Combine(Path.GetFullPath("cpp2il_out"), "types", currentAssembly.Name.Name));

            return (from il2CppTypeDefinition in imageDef.Types!
                    let type = SharedState.UnmanagedToManagedTypes[il2CppTypeDefinition]
                    let methods = ProcessTypeContents(metadata, theDll, il2CppTypeDefinition, type, imageDef)
                    select (type, methods))
                .ToList();
        }

        private static List<CppMethodData> ProcessTypeContents(Il2CppMetadata metadata, PE cppAssembly, Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition, Il2CppImageDefinition imageDef)
        {
            var typeMetaText = new StringBuilder();

            if (EmitMetadataFiles)
                typeMetaText.Append(GetBasicTypeMetadataString(ilTypeDefinition));

            MethodDefinition addressAttribute;
            MethodDefinition fieldOffsetAttribute;
            MethodDefinition tokenAttribute;

            if (!_attributesByModule.ContainsKey(ilTypeDefinition.Module))
            {
                addressAttribute = ilTypeDefinition.Module.Types.First(x => x.Name == "AddressAttribute").Methods[0];
                fieldOffsetAttribute = ilTypeDefinition.Module.Types.First(x => x.FullName == "Cpp2IlInjected.FieldOffsetAttribute").Methods[0];
                tokenAttribute = ilTypeDefinition.Module.Types.First(x => x.Name == "TokenAttribute").Methods[0];
                _attributesByModule[ilTypeDefinition.Module] = (addressAttribute, fieldOffsetAttribute, tokenAttribute);
            }
            else
            {
                (addressAttribute, fieldOffsetAttribute, tokenAttribute) = _attributesByModule[ilTypeDefinition.Module];
            }

            var stringType = ilTypeDefinition.Module.ImportReference(Utils.TryLookupTypeDefKnownNotGeneric("System.String"));

            //Token attribute
            var customTokenAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(tokenAttribute));
            customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{cppTypeDefinition.token:X}")));
            ilTypeDefinition.CustomAttributes.Add(customTokenAttribute);

            //field
            var fields = ProcessFieldsInType(cppTypeDefinition, ilTypeDefinition, stringType, fieldOffsetAttribute, tokenAttribute);
            
            if(EmitMetadataFiles)
                fields.ForEach(f => typeMetaText.Append(GetFieldMetadataString(f)));

            //Methods
            var lastMethodId = cppTypeDefinition.firstMethodIdx + cppTypeDefinition.method_count;
            var typeMethods = new List<CppMethodData>();
            Il2CppGenericContainer genericContainer;
            for (var methodId = cppTypeDefinition.firstMethodIdx; methodId < lastMethodId; ++methodId)
            {
                var methodDef = metadata.methodDefs[methodId];
                var methodReturnType = cppAssembly.types[methodDef.returnTypeIdx];
                var methodName = metadata.GetStringFromIndex(methodDef.nameIndex);
                var methodDefinition = new MethodDefinition(methodName, (MethodAttributes) methodDef.flags,
                    ilTypeDefinition.Module.ImportReference(Utils.TryLookupTypeDefByName("System.Void").Item1));

                SharedState.UnmanagedToManagedMethods[methodDef] = methodDefinition;


                var offsetInRam = LibCpp2IlMain.MetadataVersion >= 27
                    ? methodDef.MethodPointer
                    : cppAssembly.GetMethodPointer(methodDef.methodIndex, methodId, imageDef.assemblyIndex, methodDef.token); //This method is significantly faster.


                var offsetInFile = offsetInRam == 0 ? 0 : cppAssembly.MapVirtualAddressToRaw(offsetInRam);
                if (EmitMetadataFiles)
                    typeMetaText.Append($"\n\tMethod: {methodName}:\n")
                        .Append($"\t\tFile Offset 0x{offsetInFile:X8}\n")
                        .Append($"\t\tRam Offset 0x{offsetInRam:x8}\n")
                        .Append($"\t\tVirtual Method Slot: {methodDef.slot}\n");

                var bytes = new List<byte>();
                var offset = offsetInFile;
                while (true)
                {
                    var b = cppAssembly.raw[offset];
                    if (b == 0xC3 && cppAssembly.raw[offset + 1] == 0xCC)
                    {
                        bytes.Add(b);
                        break;
                    }

                    if (b == 0xCC && bytes.Count > 0 && bytes.Last() == 0xc3) break;
                    bytes.Add(b);
                    offset++;
                }

                if (EmitMetadataFiles)
                    typeMetaText.Append($"\t\tMethod Length: {bytes.Count} bytes\n");

                typeMethods.Add(new CppMethodData
                {
                    MethodName = methodName,
                    MethodId = methodId,
                    MethodBytes = bytes.ToArray(),
                    MethodOffsetRam = offsetInRam
                });


                ilTypeDefinition.Methods.Add(methodDefinition);
                methodDefinition.ReturnType = Utils.ImportTypeInto(methodDefinition, methodReturnType);

                customTokenAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(tokenAttribute));
                customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{methodDef.token:X}")));
                methodDefinition.CustomAttributes.Add(customTokenAttribute);

                if (methodDefinition.HasBody && ilTypeDefinition.BaseType?.FullName != "System.MulticastDelegate")
                {
                    var ilprocessor = methodDefinition.Body.GetILProcessor();
                    if (methodDefinition.ReturnType.FullName == "System.Void")
                    {
                        ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
                    }
                    else if (methodDefinition.ReturnType.IsValueType)
                    {
                        var variable = new VariableDefinition(methodDefinition.ReturnType);
                        methodDefinition.Body.Variables.Add(variable);
                        ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloca_S, variable));
                        ilprocessor.Append(ilprocessor.Create(OpCodes.Initobj, methodDefinition.ReturnType));
                        ilprocessor.Append(ilprocessor.Create(OpCodes.Ldloc_0));
                        ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
                    }
                    else
                    {
                        ilprocessor.Append(ilprocessor.Create(OpCodes.Ldnull));
                        ilprocessor.Append(ilprocessor.Create(OpCodes.Ret));
                    }
                }

                SharedState.MethodsByIndex.Add(methodId, methodDefinition);
                //Method Params
                for (var paramIdx = 0; paramIdx < methodDef.parameterCount; ++paramIdx)
                {
                    var parameterDef = metadata.parameterDefs[methodDef.parameterStart + paramIdx];
                    var parameterName = metadata.GetStringFromIndex(parameterDef.nameIndex);
                    var parameterType = cppAssembly.types[parameterDef.typeIndex];
                    var parameterTypeRef = Utils.ImportTypeInto(methodDefinition, parameterType);

                    ParameterDefinition parameterDefinition;
                    if (parameterTypeRef is GenericParameter genericParameter)
                        parameterDefinition = new ParameterDefinition(genericParameter);
                    else
                        parameterDefinition = new ParameterDefinition(parameterName, (ParameterAttributes) parameterType.attrs, parameterTypeRef);

                    methodDefinition.Parameters.Add(parameterDefinition);
                    //Default values for params
                    if (parameterDefinition.HasDefault)
                    {
                        var parameterDefault = metadata.GetParameterDefaultValueFromIndex(methodDef.parameterStart + paramIdx);
                        if (parameterDefault != null && parameterDefault.dataIndex != -1)
                        {
                            parameterDefinition.Constant = LibCpp2ILUtils.GetDefaultValue(parameterDefault.dataIndex, parameterDefault.typeIndex);
                        }
                    }


                    if (EmitMetadataFiles)
                        typeMetaText.Append($"\n\t\tParameter {paramIdx}:\n")
                            .Append($"\t\t\tName: {parameterName}\n")
                            .Append($"\t\t\tType: {(parameterTypeRef.Namespace == "" ? "<None>" : parameterTypeRef.Namespace)}.{parameterTypeRef.Name}\n")
                            .Append($"\t\t\tDefault Value: {parameterDefinition.Constant}");
                }


                var methodPointer = LibCpp2IlMain.MetadataVersion >= 27
                    ? methodDef.MethodPointer
                    : cppAssembly.GetMethodPointer(methodDef.methodIndex, methodId, imageDef.assemblyIndex, methodDef.token); //This method is significantly faster.

                //Address attribute
                if (methodPointer > 0)
                {
                    var customAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(addressAttribute));
                    var fixedMethodPointer = LibCpp2IlMain.ThePe.GetRVA(methodPointer);
                    var rva = new CustomAttributeNamedArgument("RVA", new CustomAttributeArgument(stringType, $"0x{fixedMethodPointer:X}"));
                    var offsetArg = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{LibCpp2IlMain.ThePe.MapVirtualAddressToRaw(methodPointer):X}"));
                    var va = new CustomAttributeNamedArgument("VA", new CustomAttributeArgument(stringType, $"0x{methodPointer:X}"));
                    customAttribute.Fields.Add(rva);
                    customAttribute.Fields.Add(offsetArg);
                    customAttribute.Fields.Add(va);
                    if (methodDef.slot != ushort.MaxValue)
                    {
                        var slot = new CustomAttributeNamedArgument("Slot", new CustomAttributeArgument(stringType, methodDef.slot.ToString()));
                        customAttribute.Fields.Add(slot);
                    }

                    methodDefinition.CustomAttributes.Add(customAttribute);
                }

                if (methodDef.genericContainerIndex >= 0)
                {
                    genericContainer = metadata.genericContainers[methodDef.genericContainerIndex];
                    if (genericContainer.type_argc > methodDefinition.GenericParameters.Count)
                    {
                        for (var j = 0; j < genericContainer.type_argc; j++)
                        {
                            var genericParameterIndex = genericContainer.genericParameterStart + j;
                            var param = metadata.genericParameters[genericParameterIndex];
                            var genericName = metadata.GetStringFromIndex(param.nameIndex);
                            if (!SharedState.GenericParamsByIndex.TryGetValue(genericParameterIndex,
                                out var genericParameter))
                            {
                                genericParameter = new GenericParameter(genericName, methodDefinition);
                                methodDefinition.GenericParameters.Add(genericParameter);
                                SharedState.GenericParamsByIndex.Add(genericParameterIndex, genericParameter);
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

                if (methodDef.slot < ushort.MaxValue)
                {
                    SharedState.VirtualMethodsBySlot[methodDef.slot] = methodDefinition;
                }

                SharedState.MethodsByAddress[offsetInRam] = methodDefinition;
            }

            //Properties
            var lastPropertyId = cppTypeDefinition.firstPropertyId + cppTypeDefinition.propertyCount;
            for (var propertyId = cppTypeDefinition.firstPropertyId; propertyId < lastPropertyId; ++propertyId)
            {
                var propertyDef = metadata.propertyDefs[propertyId];
                var propertyName = metadata.GetStringFromIndex(propertyDef.nameIndex);
                TypeReference propertyType = null;
                MethodDefinition getter = null;
                MethodDefinition setter = null;
                if (propertyDef.get >= 0)
                {
                    getter = SharedState.MethodsByIndex[cppTypeDefinition.firstMethodIdx + propertyDef.get];
                    propertyType = getter.ReturnType;
                }

                if (propertyDef.set >= 0)
                {
                    setter = SharedState.MethodsByIndex[cppTypeDefinition.firstMethodIdx + propertyDef.set];
                    if (propertyType == null)
                        propertyType = setter.Parameters[0].ParameterType;
                }

                var propertyDefinition = new PropertyDefinition(propertyName, (PropertyAttributes) propertyDef.attrs, propertyType)
                {
                    GetMethod = getter,
                    SetMethod = setter
                };

                customTokenAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(tokenAttribute));
                customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{propertyDef.token:X}")));
                propertyDefinition.CustomAttributes.Add(customTokenAttribute);

                ilTypeDefinition.Properties.Add(propertyDefinition);
            }

            //Events
            var lastEventId = cppTypeDefinition.firstEventId + cppTypeDefinition.eventCount;
            for (var eventId = cppTypeDefinition.firstEventId; eventId < lastEventId; ++eventId)
            {
                var eventDef = metadata.eventDefs[eventId];
                var eventName = metadata.GetStringFromIndex(eventDef.nameIndex);
                var eventType = cppAssembly.types[eventDef.typeIndex];
                var eventTypeRef = Utils.ImportTypeInto(ilTypeDefinition, eventType);
                var eventDefinition = new EventDefinition(eventName, (EventAttributes) eventType.attrs, eventTypeRef);
                if (eventDef.add >= 0)
                    eventDefinition.AddMethod = SharedState.MethodsByIndex[cppTypeDefinition.firstMethodIdx + eventDef.add];
                if (eventDef.remove >= 0)
                    eventDefinition.RemoveMethod = SharedState.MethodsByIndex[cppTypeDefinition.firstMethodIdx + eventDef.remove];
                if (eventDef.raise >= 0)
                    eventDefinition.InvokeMethod = SharedState.MethodsByIndex[cppTypeDefinition.firstMethodIdx + eventDef.raise];

                customTokenAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(tokenAttribute));
                customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{eventDef.token:X}")));
                eventDefinition.CustomAttributes.Add(customTokenAttribute);

                ilTypeDefinition.Events.Add(eventDefinition);
            }

            if (EmitMetadataFiles)
                File.WriteAllText(Path.Combine(Path.GetFullPath("cpp2il_out"), "types", ilTypeDefinition.Module.Assembly.Name.Name, ilTypeDefinition.Name.Replace("<", "_").Replace(">", "_").Replace("|", "_") + "_metadata.txt"), typeMetaText.ToString());

            if (cppTypeDefinition.genericContainerIndex < 0) return typeMethods; //Finished processing if not generic

            genericContainer = metadata.genericContainers[cppTypeDefinition.genericContainerIndex];
            if (genericContainer.type_argc <= ilTypeDefinition.GenericParameters.Count) return typeMethods; //Finished processing

            for (var i = 0; i < genericContainer.type_argc; i++)
            {
                var genericParameterIndex = genericContainer.genericParameterStart + i;
                var param = metadata.genericParameters[genericParameterIndex];
                var genericName = metadata.GetStringFromIndex(param.nameIndex);
                if (!SharedState.GenericParamsByIndex.TryGetValue(genericParameterIndex, out var genericParameter))
                {
                    genericParameter = new GenericParameter(genericName, ilTypeDefinition);
                    ilTypeDefinition.GenericParameters.Add(genericParameter);
                    SharedState.GenericParamsByIndex.Add(genericParameterIndex, genericParameter);
                }
                else
                {
                    if (ilTypeDefinition.GenericParameters.Contains(genericParameter)) continue;
                    ilTypeDefinition.GenericParameters.Add(genericParameter);
                }
            }

            return typeMethods;
        }

        private static List<FieldInType> ProcessFieldsInType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition, TypeReference stringType, MethodDefinition fieldOffsetAttribute, MethodDefinition tokenAttribute)
        {
            var fields = new List<FieldInType>();

            var counter = -1;
            foreach (var fieldDef in cppTypeDefinition.Fields!)
            {
                counter++;
                var fieldTypeRef = Utils.ImportTypeInto(ilTypeDefinition, fieldDef.RawFieldType!);

                var fieldDefinition = new FieldDefinition(fieldDef.Name, (FieldAttributes) fieldDef.RawFieldType.attrs, fieldTypeRef);

                ilTypeDefinition.Fields.Add(fieldDefinition);

                SharedState.UnmanagedToManagedFields[fieldDef] = fieldDefinition;
                SharedState.ManagedToUnmanagedFields[fieldDefinition] = fieldDef;

                //Field default values
                if (fieldDefinition.HasDefault)
                {
                    fieldDefinition.Constant = fieldDef.DefaultValue?.Value;
                }

                if ((fieldDef.RawFieldType.attrs & (int) FieldAttributes.HasFieldRVA) != 0)
                {
                    fieldDefinition.InitialValue = fieldDef.StaticArrayInitialValue;
                }

                var thisFieldOffset = LibCpp2IlMain.ThePe!.GetFieldOffsetFromIndex(cppTypeDefinition.TypeIndex, counter, fieldDef.FieldIndex, ilTypeDefinition.IsValueType, fieldDefinition.IsStatic);
                fields.Add(GetFieldInType(fieldTypeRef, thisFieldOffset, fieldDef.Name!, fieldDefinition));

                if (!fieldDefinition.IsStatic)
                {
                    //Add [FieldOffset(Offset = "0xDEADBEEF")]
                    var fieldOffsetAttributeInst = new CustomAttribute(ilTypeDefinition.Module.ImportReference(fieldOffsetAttribute));
                    fieldOffsetAttributeInst.Fields.Add(new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{fields.Last().Offset:X}")));
                    fieldDefinition.CustomAttributes.Add(fieldOffsetAttributeInst);
                }

                var customTokenAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(tokenAttribute));
                customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{fieldDef.token:X}")));
                fieldDefinition.CustomAttributes.Add(customTokenAttribute);
            }

            fields.Sort(); //By offset
            SharedState.FieldsByType[ilTypeDefinition] = fields;

            return fields;
        }

        private static FieldInType GetFieldInType(TypeReference fieldTypeRef, int fieldOffset, string fieldName, FieldDefinition fieldDefinition)
        {
            //ONE correction. String#start_char is remapped to a char[] not a char because the block allocated for all chars is directly sequential to the length of the string, because that's how c++ works.
            if (fieldDefinition.DeclaringType.FullName == "System.String" && fieldTypeRef.FullName == "System.Char")
                fieldTypeRef = fieldTypeRef.MakeArrayType();

            var field = new FieldInType
            {
                Name = fieldName,
                FieldType = fieldTypeRef,
                Offset = (ulong) fieldOffset,
                Static = fieldDefinition.IsStatic,
                Constant = fieldDefinition.Constant,
                DeclaringType = fieldDefinition.DeclaringType,
                Definition = fieldDefinition,
            };

            return field;
        }

        private static StringBuilder GetBasicTypeMetadataString(TypeDefinition ilTypeDefinition)
        {
            StringBuilder ret = new StringBuilder();
            ret.Append($"Type: {ilTypeDefinition.FullName}:")
                .Append($"\n\tBase Class: \n\t\t{ilTypeDefinition.BaseType}\n")
                .Append("\n\tInterfaces:\n");

            foreach (var implementation in ilTypeDefinition.Interfaces)
            {
                ret.Append($"\t\t{implementation.InterfaceType.FullName}\n");
            }

            if (ilTypeDefinition.NestedTypes.Count > 0)
            {
                ret.Append("\n\tNested Types:\n");

                foreach (var nestedType in ilTypeDefinition.NestedTypes)
                {
                    ret.Append($"\t\t{nestedType.FullName}\n");
                }
            }

            return ret;
        }

        private static StringBuilder GetFieldMetadataString(FieldInType field)
        {
            var ret = new StringBuilder();
            ret.Append($"\n\t{(field.Static ? "Static Field" : "Field")}: {field.Name}\n")
                .Append($"\t\tType: {field.FieldType?.FullName}\n")
                .Append($"\t\tOffset in Defining Type: 0x{field.Offset:X}\n")
                .Append($"\t\tHas Default: {field.Definition.HasDefault}\n");

            if (field.Constant is char c && char.IsSurrogate(c)) 
                return ret;

            if (field.Constant != null)
                ret.Append($"\t\tDefault Value: {field.Constant}\n");
            
            return ret;
        }
    }
}