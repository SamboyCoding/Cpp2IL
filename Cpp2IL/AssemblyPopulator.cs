using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Cpp2IL.Analysis;
using LibCpp2IL;
using LibCpp2IL.Metadata;
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
        internal const string InjectedNamespaceName = "Cpp2IlInjected";
        private static readonly Dictionary<ModuleDefinition, (MethodDefinition, MethodDefinition, MethodDefinition)> _attributesByModule = new Dictionary<ModuleDefinition, (MethodDefinition, MethodDefinition, MethodDefinition)>();

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
                module.ImportReference(Utils.TryLookupTypeDefKnownNotGeneric("System.Void"))
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

        public static void PopulateStubTypesInAssembly(Il2CppImageDefinition imageDef)
        {
            var firstTypeDefinition = SharedState.TypeDefsByIndex[imageDef.firstTypeIndex];
            var currentAssembly = firstTypeDefinition.Module.Assembly;

            InjectCustomAttributes(currentAssembly);

            foreach (var il2CppTypeDefinition in imageDef.Types!)
            {
                var managedType = SharedState.UnmanagedToManagedTypes[il2CppTypeDefinition];
                CopyIl2CppDataToManagedType(il2CppTypeDefinition, managedType);
            }
        }

        public static void FixupExplicitOverridesInAssembly(Il2CppImageDefinition imageDef)
        {
            foreach (var il2CppTypeDefinition in imageDef.Types!)
            {
                var managedType = SharedState.UnmanagedToManagedTypes[il2CppTypeDefinition];
                FixupExplicitOverridesInType(managedType);
            }
        }

        private static void FixupExplicitOverridesInType(TypeDefinition ilTypeDefinition)
        {
            //Fixup explicit Override (e.g. System.Collections.Generic.Dictionary`2's IDictionary.Add method) methods.
            foreach (var methodDefinition in ilTypeDefinition.Methods)
            {
                var methodDef = SharedState.ManagedToUnmanagedMethods[methodDefinition];
                
                //The two StartsWith calls are for a) .ctor / .cctor and b) compiler-generated enumerator methods for these two methods.
                if (!methodDef.Name!.Contains(".") || methodDef.Name.StartsWith(".") || methodDef.Name.StartsWith("<")) continue;

                //Helpfully, the full name of the method is actually the full name of the base method. Unless generics come into play.
                var baseMethodType = methodDef.Name[..methodDef.Name.LastIndexOf(".", StringComparison.Ordinal)];
                var baseMethodName = methodDef.Name[(methodDef.Name.LastIndexOf(".", StringComparison.Ordinal) + 1)..];

                //Unfortunately, the only way we can get these types is by name - there is no metadata reference.
                var (baseType, genericParamNames) = Utils.TryLookupTypeDefByName(baseMethodType);

                if (baseType == null) 
                    continue;
                        
                MethodReference? baseRef = null;
                if (genericParamNames.Length == 0)
                    baseRef = baseType.Methods.Single(m => m.Name == baseMethodName && m.Parameters.Count == methodDefinition.Parameters.Count);
                else
                {
                    var nonGenericRef = baseType.Methods.Single(m => m.Name == baseMethodName && m.Parameters.Count == methodDefinition.Parameters.Count);

                    var genericParams = genericParamNames
                        .Select(g =>
                            (TypeReference?) Utils.TryLookupTypeDefKnownNotGeneric(g)
                            ?? GenericInstanceUtils.ResolveGenericParameterType(new GenericParameter(g, baseType), ilTypeDefinition)
                        )
                        .ToList();

                    if (genericParams.All(gp => gp != null))
                    {
                        baseRef = nonGenericRef.MakeGeneric(genericParams.ToArray()!); //Non-null assertion because we've null-checked the params above.
                    }
                    else
                    {
                        var failedIdx = genericParams.FindIndex(g => g == null);
                        Console.WriteLine($"\tWarning: Failed to resolve generic parameter \"{genericParamNames[failedIdx]}\" for base method override {methodDef.Name}.");
                        continue; //Move to next method.
                    }
                }

                if (baseRef != null)
                    methodDefinition.Overrides.Add(ilTypeDefinition.Module.ImportReference(baseRef, methodDefinition));
                else
                    Console.WriteLine($"\tWarning: Failed to resolve base method override in type {ilTypeDefinition.FullName}: Type {baseMethodType} / Name {baseMethodName}");
            }
        }

        private static void CopyIl2CppDataToManagedType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition)
        {
            var (addressAttribute, fieldOffsetAttribute, tokenAttribute) = GetInjectedAttributes(ilTypeDefinition);

            var stringType = ilTypeDefinition.Module.ImportReference(Utils.TryLookupTypeDefKnownNotGeneric("System.String"));

            //Token attribute
            var customTokenAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(tokenAttribute));
            customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{cppTypeDefinition.token:X}")));
            ilTypeDefinition.CustomAttributes.Add(customTokenAttribute);

            if (cppTypeDefinition.GenericContainer != null)
            {
                //Type generic params.
                foreach (var param in cppTypeDefinition.GenericContainer.GenericParameters)
                {
                    if (!SharedState.GenericParamsByIndex.TryGetValue(param.Index, out var p))
                    {
                        p = new GenericParameter(param.Name, ilTypeDefinition);
                        SharedState.GenericParamsByIndex[param.Index] = p;
                    }

                    if (!ilTypeDefinition.GenericParameters.Contains(p))
                        ilTypeDefinition.GenericParameters.Add(p);
                }
            }

            //Fields
            ProcessFieldsInType(cppTypeDefinition, ilTypeDefinition, stringType, fieldOffsetAttribute, tokenAttribute);

            //Methods
            ProcessMethodsInType(cppTypeDefinition, ilTypeDefinition, tokenAttribute, addressAttribute, stringType);

            //Properties
            ProcessPropertiesInType(cppTypeDefinition, ilTypeDefinition, tokenAttribute, stringType);

            //Events
            ProcessEventsInType(cppTypeDefinition, ilTypeDefinition, tokenAttribute, stringType);
        }

        private static (MethodDefinition addressAttribute, MethodDefinition fieldOffsetAttribute, MethodDefinition tokenAttribute) GetInjectedAttributes(TypeDefinition ilTypeDefinition)
        {
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

            return (addressAttribute, fieldOffsetAttribute, tokenAttribute);
        }

        private static void ProcessFieldsInType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition, TypeReference stringType, MethodDefinition fieldOffsetAttribute, MethodDefinition tokenAttribute)
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

                //Field Initial Values (used for allocation of Array Literals)
                if ((fieldDef.RawFieldType.attrs & (int) FieldAttributes.HasFieldRVA) != 0)
                {
                    fieldDefinition.InitialValue = fieldDef.StaticArrayInitialValue;
                }

                var thisFieldOffset = LibCpp2IlMain.Binary!.GetFieldOffsetFromIndex(cppTypeDefinition.TypeIndex, counter, fieldDef.FieldIndex, ilTypeDefinition.IsValueType, fieldDefinition.IsStatic);
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
        }

        private static void ProcessMethodsInType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition, MethodDefinition tokenAttribute, MethodDefinition addressAttribute, TypeReference stringType)
        {
            foreach (var methodDef in cppTypeDefinition.Methods!)
            {
                var methodReturnType = methodDef.RawReturnType!;

                var methodDefinition = new MethodDefinition(methodDef.Name, (MethodAttributes) methodDef.flags,
                    ilTypeDefinition.Module.ImportReference(Utils.TryLookupTypeDefKnownNotGeneric("System.Void")));

                SharedState.UnmanagedToManagedMethods[methodDef] = methodDefinition;
                SharedState.ManagedToUnmanagedMethods[methodDefinition] = methodDef;

                ilTypeDefinition.Methods.Add(methodDefinition);
                methodDefinition.ReturnType = Utils.ImportTypeInto(methodDefinition, methodReturnType);

                CustomAttribute customTokenAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(tokenAttribute));
                customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{methodDef.token:X}")));
                methodDefinition.CustomAttributes.Add(customTokenAttribute);

                if (methodDefinition.HasBody && ilTypeDefinition.BaseType?.FullName != "System.MulticastDelegate")
                    FillMethodBodyWithStub(methodDefinition);

                SharedState.MethodsByIndex[methodDef.MethodIndex] = methodDefinition;
                SharedState.MethodsByAddress[methodDef.MethodPointer] = methodDefinition;

                //Method Params
                HandleMethodParameters(methodDef, methodDefinition);

                var methodPointer = methodDef.MethodPointer;

                //Address attribute
                if (methodPointer > 0)
                {
                    var customAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(addressAttribute));
                    customAttribute.Fields.Add(new CustomAttributeNamedArgument("RVA", new CustomAttributeArgument(stringType, $"0x{LibCpp2IlMain.Binary.GetRVA(methodPointer):X}")));
                    customAttribute.Fields.Add(new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{LibCpp2IlMain.Binary.MapVirtualAddressToRaw(methodPointer):X}")));
                    customAttribute.Fields.Add(new CustomAttributeNamedArgument("VA", new CustomAttributeArgument(stringType, $"0x{methodPointer:X}")));
                    if (methodDef.slot != ushort.MaxValue)
                    {
                        customAttribute.Fields.Add(new CustomAttributeNamedArgument("Slot", new CustomAttributeArgument(stringType, methodDef.slot.ToString())));
                    }

                    methodDefinition.CustomAttributes.Add(customAttribute);
                }

                //Handle generic parameters.
                methodDef.GenericContainer?.GenericParameters
                    .Select(p => SharedState.GenericParamsByIndex.TryGetValue(p.Index, out var gp) ? gp : new GenericParameter(p.Name, methodDefinition))
                    .ToList()
                    .ForEach(parameter => methodDefinition.GenericParameters.Add(parameter));

                if (methodDef.slot < ushort.MaxValue)
                    SharedState.VirtualMethodsBySlot[methodDef.slot] = methodDefinition;
            }
        }

        private static void ProcessPropertiesInType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition, MethodDefinition tokenAttribute, TypeReference stringType)
        {
            foreach (var propertyDef in cppTypeDefinition.Properties!)
            {
                var getter = propertyDef.Getter?.AsManaged();
                var setter = propertyDef.Setter?.AsManaged();

                var propertyDefinition = new PropertyDefinition(propertyDef.Name, (PropertyAttributes) propertyDef.attrs, getter?.ReturnType ?? setter?.ReturnType)
                {
                    GetMethod = getter,
                    SetMethod = setter
                };

                var customTokenAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(tokenAttribute));
                customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{propertyDef.token:X}")));
                propertyDefinition.CustomAttributes.Add(customTokenAttribute);

                ilTypeDefinition.Properties.Add(propertyDefinition);
            }
        }

        private static void ProcessEventsInType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition, MethodDefinition tokenAttribute, TypeReference stringType)
        {
            foreach (var il2cppEventDef in cppTypeDefinition.Events!)
            {
                var monoDef = new EventDefinition(il2cppEventDef.Name, (EventAttributes) il2cppEventDef.EventAttributes, Utils.ImportTypeInto(ilTypeDefinition, il2cppEventDef.RawType!))
                {
                    AddMethod = il2cppEventDef.Adder?.AsManaged(),
                    RemoveMethod = il2cppEventDef.Remover?.AsManaged(),
                    InvokeMethod = il2cppEventDef.Invoker?.AsManaged()
                };

                var customTokenAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(tokenAttribute));
                customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{il2cppEventDef.token:X}")));
                monoDef.CustomAttributes.Add(customTokenAttribute);

                ilTypeDefinition.Events.Add(monoDef);
            }
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

        private static void FillMethodBodyWithStub(MethodDefinition methodDefinition)
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

        private static void HandleMethodParameters(Il2CppMethodDefinition il2CppMethodDef, MethodDefinition monoMethodDef)
        {
            foreach (var il2cppParam in il2CppMethodDef.Parameters!)
            {
                var parameterTypeRef = Utils.ImportTypeInto(monoMethodDef, il2cppParam.RawType);

                ParameterDefinition monoParam;
                if (parameterTypeRef is GenericParameter genericParameter)
                    monoParam = new ParameterDefinition(genericParameter);
                else
                    monoParam = new ParameterDefinition(il2cppParam.ParameterName, (ParameterAttributes) il2cppParam.ParameterAttributes, parameterTypeRef);

                if (il2cppParam.DefaultValue != null)
                    monoParam.Constant = il2cppParam.DefaultValue;

                monoMethodDef.Parameters.Add(monoParam);
            }
        }

        internal static string BuildWholeMetadataString(TypeDefinition typeDefinition)
        {
            var ret = new StringBuilder();

            ret.Append(GetBasicTypeMetadataString(typeDefinition));

            SharedState.FieldsByType[typeDefinition].ToList().ForEach(f => ret.Append(GetFieldMetadataString(f)));

            typeDefinition.Methods.ToList().ForEach(m => ret.Append(GetMethodMetadataString(m.AsUnmanaged())));

            return ret.ToString();
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

        private static StringBuilder GetMethodMetadataString(Il2CppMethodDefinition methodDef)
        {
            var typeMetaText = new StringBuilder();
            typeMetaText.Append($"\n\tMethod: {methodDef.Name}:\n")
                .Append($"\t\tFile Offset 0x{methodDef.MethodOffsetInFile:X8}\n")
                .Append($"\t\tRam Offset 0x{methodDef.MethodPointer:x8}\n")
                .Append($"\t\tVirtual Method Slot: {methodDef.slot}\n");

            var counter = -1;
            foreach (var parameter in methodDef.Parameters!)
            {
                counter++;
                typeMetaText.Append($"\n\t\tParameter {counter}:\n")
                    .Append($"\t\t\tName: {parameter.ParameterName}\n")
                    .Append($"\t\t\tType: {parameter.Type}\n")
                    .Append($"\t\t\tDefault Value: {parameter.DefaultValue}");
            }

            return typeMetaText;
        }
    }
}