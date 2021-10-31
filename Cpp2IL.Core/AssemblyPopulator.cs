using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Analysis;
using Cpp2IL.Core.Utils;
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

namespace Cpp2IL.Core
{
    internal static class AssemblyPopulator
    {
        internal const string InjectedNamespaceName = "Cpp2IlInjected";
        private static readonly Dictionary<ModuleDefinition, (MethodDefinition, MethodDefinition, MethodDefinition)> _attributesByModule = new Dictionary<ModuleDefinition, (MethodDefinition, MethodDefinition, MethodDefinition)>();

        internal static void Reset() => _attributesByModule.Clear();

        public static void ConfigureHierarchy()
        {
            foreach (var typeDefinition in SharedState.AllTypeDefinitions)
            {
                var il2cppTypeDef = SharedState.ManagedToUnmanagedTypes[typeDefinition];
                
                //Type generic params.
                PopulateGenericParamsForType(il2cppTypeDef, typeDefinition);

                //Set base type
                if (il2cppTypeDef.RawBaseType is { } parent)
                    typeDefinition.BaseType = Utils.Utils.ImportTypeInto(typeDefinition, parent);

                //Set interfaces
                foreach (var interfaceType in il2cppTypeDef.RawInterfaces)
                    typeDefinition.Interfaces.Add(new InterfaceImplementation(Utils.Utils.ImportTypeInto(typeDefinition, interfaceType)));
            }
        }

        private static void CreateDefaultConstructor(TypeDefinition typeDefinition)
        {
            var module = typeDefinition.Module;
            var defaultConstructor = new MethodDefinition(
                ".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.ImportReference(Utils.Utils.TryLookupTypeDefKnownNotGeneric("System.Void"))
            );

            var processor = defaultConstructor.Body.GetILProcessor();

            var ctor = Utils.Utils.TryLookupTypeDefKnownNotGeneric("System.Attribute").GetConstructors().FirstOrDefault();

            if (ctor != null)
            {
                //Can be null if we're on mscorlib, thus attribute hasn't been initialized yet.
                processor.Emit(OpCodes.Ldarg_0);
                processor.Emit(OpCodes.Call, module.ImportReference(ctor));
            }

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

        private static void InjectOurTypes(AssemblyDefinition imageDef, bool suppressAttributes)
        {
            var stringTypeReference = imageDef.MainModule.ImportReference(Utils.Utils.TryLookupTypeDefKnownNotGeneric("System.String"));
            var attributeTypeReference = imageDef.MainModule.ImportReference(Utils.Utils.TryLookupTypeDefKnownNotGeneric("System.Attribute"));
            var exceptionTypeReference = imageDef.MainModule.ImportReference(Utils.Utils.TryLookupTypeDefKnownNotGeneric("System.Exception"));

            if (!suppressAttributes)
            {
                InjectAttribute("AddressAttribute", stringTypeReference, attributeTypeReference, imageDef, "RVA", "Offset", "VA", "Slot");
                InjectAttribute("FieldOffsetAttribute", stringTypeReference, attributeTypeReference, imageDef, "Offset");
                InjectAttribute("AttributeAttribute", stringTypeReference, attributeTypeReference, imageDef, "Name", "RVA", "Offset");
                InjectAttribute("MetadataOffsetAttribute", stringTypeReference, attributeTypeReference, imageDef, "Offset");
                InjectAttribute("TokenAttribute", stringTypeReference, attributeTypeReference, imageDef, "Token");
            }

            var analysisFailedExceptionType = new TypeDefinition(InjectedNamespaceName, "AnalysisFailedException", (TypeAttributes)0x100001, exceptionTypeReference);
            var defaultConstructor = new MethodDefinition(
                ".ctor",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                imageDef.MainModule.ImportReference(Utils.Utils.TryLookupTypeDefKnownNotGeneric("System.Void"))
            );
            
            defaultConstructor.Parameters.Add(new("message", ParameterAttributes.None, stringTypeReference));

            var exceptionTypeDef = exceptionTypeReference.Resolve();
            if (exceptionTypeDef.Methods.FirstOrDefault(m => m.IsConstructor && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "String") is { } parentCtor)
            {
                //This block serves to not create this body on mscorlib because the method isn't initialized yet. 
                var prc = defaultConstructor.Body.GetILProcessor();

                prc.Emit(OpCodes.Ldarg_0); //load this
                prc.Emit(OpCodes.Ldarg_1); //load message
                prc.Emit(OpCodes.Call, imageDef.MainModule.ImportReference(parentCtor)); //call super ctor
                prc.Emit(OpCodes.Ret); //return
            }

            analysisFailedExceptionType.Methods.Add(defaultConstructor);
            
            imageDef.MainModule.Types.Add(analysisFailedExceptionType);
        }

        public static void PopulateStubTypesInAssembly(Il2CppImageDefinition imageDef, bool suppressAttributes)
        {
            var firstTypeDefinition = SharedState.TypeDefsByIndex[imageDef.firstTypeIndex];
            var currentAssembly = firstTypeDefinition.Module.Assembly;
            
                InjectOurTypes(currentAssembly, suppressAttributes);

            foreach (var il2CppTypeDefinition in imageDef.Types!)
            {
                var managedType = SharedState.UnmanagedToManagedTypes[il2CppTypeDefinition];

                try
                {
                    CopyIl2CppDataToManagedType(il2CppTypeDefinition, managedType, suppressAttributes);
                }
                catch (Exception e)
                {
                    throw new Exception($"Failed to process type {managedType.FullName} (module {managedType.Module.Name}, declaring type {managedType.DeclaringType?.FullName}) in {imageDef.Name}", e);
                }
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
            foreach (var currentlyFixingUp in ilTypeDefinition.Methods)
            {
                var methodDef = currentlyFixingUp.AsUnmanaged();

                //The two StartsWith calls are for a) .ctor / .cctor and b) compiler-generated enumerator methods for these two methods.
                if (!methodDef.Name!.Contains(".") || methodDef.Name.StartsWith(".") || methodDef.Name.StartsWith("<")) continue;

                //Helpfully, the full name of the method is actually the full name of the base method. Unless generics come into play.
                var baseMethodType = methodDef.Name[..methodDef.Name.LastIndexOf(".", StringComparison.Ordinal)];
                var baseMethodName = methodDef.Name[(methodDef.Name.LastIndexOf(".", StringComparison.Ordinal) + 1)..];

                //Unfortunately, the only way we can get these types is by name - there is no metadata reference.
                var (baseType, genericParamNames) = Utils.Utils.TryLookupTypeDefByName(baseMethodType);

                if (baseType == null)
                {
                    Logger.WarnNewline($"\tFailed to resolve base type {baseMethodType} for base method override {methodDef.Name}");
                    continue;
                }

                var targetParameters = currentlyFixingUp.Parameters.Select(p => p.ParameterType.FullName).ToArray();
                MethodReference? baseRef;
                if (genericParamNames.Length == 0)
                    baseRef = baseType.Methods.SingleOrDefault(m =>
                        m.Name == baseMethodName && m.Parameters.Count == currentlyFixingUp.Parameters.Count && m.ReturnType.FullName == currentlyFixingUp.ReturnType.FullName && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(targetParameters));
                else
                {
                    MethodDefinition nonGenericRef;
                    try
                    {
                        nonGenericRef = baseType.Methods.Single(m => m.Name == baseMethodName && m.Parameters.Count == currentlyFixingUp.Parameters.Count);
                    }
                    catch (InvalidOperationException)
                    {
                        //More than one match - log a warning and skip
                        Logger.WarnNewline($"\tMore than one potential base method for base type \"{baseType.FullName}\", method name \"{baseMethodName}\", parameter count {currentlyFixingUp.Parameters.Count}, while considering explicit override {currentlyFixingUp.FullName}");
                        continue;
                    }

                    TypeReference? ResolveGenericParameter(string name)
                    {
                        var (type, gParams) = Utils.Utils.TryLookupTypeDefByName(name);
                        if (type == null) 
                            return GenericInstanceUtils.ResolveGenericParameterType(new GenericParameter(name, baseType), ilTypeDefinition);

                        if (gParams.Length > 0)
                        {
                            var parameterRefs = gParams.Select(ResolveGenericParameter).ToArray();

                            if (parameterRefs.Any(gp => gp == null))
                                return null;
                            
                            return ilTypeDefinition.Module.ImportRecursive(type.MakeGenericInstanceType(parameterRefs));
                        }
                            
                        return type;
                    }

                    var genericParams = genericParamNames.Select(ResolveGenericParameter).ToList();

                    if (genericParams.All(gp => gp != null))
                    {
                        //Non-null assertion because we've null-checked the params above.
                        genericParams = genericParams.Select(p => p is GenericParameter ? p : ilTypeDefinition.Module.ImportReference(p, currentlyFixingUp)).ToList()!;
                        baseRef = nonGenericRef.MakeMethodOnGenericType(genericParams.ToArray()!);
                    }
                    else
                    {
                        var failedIdx = genericParams.FindIndex(g => g == null);
                        Logger.WarnNewline($"\tFailed to resolve generic parameter \"{genericParamNames[failedIdx]}\" for base method override {methodDef.Name}.");
                        continue; //Move to next method.
                    }
                }

                if (baseRef != null)
                {
                    // Logger.InfoNewline($"Added override for type {ilTypeDefinition.FullName}, base type {baseMethodType} method {baseMethodName}, overriding {baseRef}");
                    currentlyFixingUp.Overrides.Add(ilTypeDefinition.Module.ImportReference(baseRef, currentlyFixingUp));
                }
                else
                    Logger.WarnNewline($"\tFailed to resolve base method override in type {ilTypeDefinition.FullName}: Type {baseMethodType} / Name {baseMethodName}");
            }
        }

        private static void CopyIl2CppDataToManagedType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition, bool suppressAttributes)
        {
            MethodDefinition? addressAttribute = null, fieldOffsetAttribute = null, tokenAttribute = null;
            if (!suppressAttributes)
                (addressAttribute, fieldOffsetAttribute, tokenAttribute) = GetInjectedAttributes(ilTypeDefinition);

            var stringType = ilTypeDefinition.Module.ImportReference(Utils.Utils.TryLookupTypeDefKnownNotGeneric("System.String"));

            if (!suppressAttributes)
            {
                //Token attribute
                var customTokenAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(tokenAttribute));
                customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{cppTypeDefinition.token:X}")));
                ilTypeDefinition.CustomAttributes.Add(customTokenAttribute);
            }

            //Type generic params.
            // PopulateGenericParamsForType(cppTypeDefinition, ilTypeDefinition);

            //Fields
            ProcessFieldsInType(cppTypeDefinition, ilTypeDefinition, stringType, fieldOffsetAttribute, tokenAttribute);

            //Methods
            ProcessMethodsInType(cppTypeDefinition, ilTypeDefinition, tokenAttribute, addressAttribute, stringType);

            //Properties
            ProcessPropertiesInType(cppTypeDefinition, ilTypeDefinition, tokenAttribute, stringType);

            //Events
            ProcessEventsInType(cppTypeDefinition, ilTypeDefinition, tokenAttribute, stringType);
        }

        private static void PopulateGenericParamsForType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition)
        {
            if (cppTypeDefinition.GenericContainer == null) 
                return;
            
            foreach (var param in cppTypeDefinition.GenericContainer.GenericParameters)
            {
                if (!SharedState.GenericParamsByIndex.TryGetValue(param.Index, out var p))
                {
                    p = new GenericParameter(param.Name, ilTypeDefinition).WithFlags(param.flags);
                    SharedState.GenericParamsByIndex[param.Index] = p;

                    ilTypeDefinition.GenericParameters.Add(p);

                    param.ConstraintTypes!
                        .Select(c => new GenericParameterConstraint(Utils.Utils.ImportTypeInto(ilTypeDefinition, c)))
                        .ToList()
                        .ForEach(p.Constraints.Add);
                }
                else if (!ilTypeDefinition.GenericParameters.Contains(p))
                    ilTypeDefinition.GenericParameters.Add(p);
            }
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

        private static void ProcessFieldsInType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition, TypeReference stringType, MethodDefinition? fieldOffsetAttribute, MethodDefinition? tokenAttribute)
        {
            var fields = new List<FieldInType>();

            var counter = -1;
            foreach (var fieldDef in cppTypeDefinition.Fields!)
            {
                counter++;
                var fieldTypeRef = Utils.Utils.ImportTypeInto(ilTypeDefinition, fieldDef.RawFieldType!);

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

                if (!fieldDefinition.IsStatic && fieldOffsetAttribute != null)
                {
                    //Add [FieldOffset(Offset = "0xDEADBEEF")]
                    var fieldOffsetAttributeInst = new CustomAttribute(ilTypeDefinition.Module.ImportReference(fieldOffsetAttribute));
                    fieldOffsetAttributeInst.Fields.Add(new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{fields.Last().Offset:X}")));
                    fieldDefinition.CustomAttributes.Add(fieldOffsetAttributeInst);
                }

                if (tokenAttribute != null)
                {
                    var customTokenAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(tokenAttribute));
                    customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{fieldDef.token:X}")));
                    fieldDefinition.CustomAttributes.Add(customTokenAttribute);
                }
            }

            fields.Sort(); //By offset
            SharedState.FieldsByType[ilTypeDefinition] = fields;
        }

        private static void ProcessMethodsInType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition, MethodDefinition? tokenAttribute, MethodDefinition? addressAttribute, TypeReference stringType)
        {
            foreach (var methodDef in cppTypeDefinition.Methods!)
            {
                var methodReturnType = methodDef.RawReturnType!;

                var methodDefinition = new MethodDefinition(methodDef.Name, (MethodAttributes) methodDef.flags,
                    ilTypeDefinition.Module.ImportReference(Utils.Utils.TryLookupTypeDefKnownNotGeneric("System.Void")));

                SharedState.UnmanagedToManagedMethods[methodDef] = methodDefinition;
                SharedState.ManagedToUnmanagedMethods[methodDefinition] = methodDef;

                ilTypeDefinition.Methods.Add(methodDefinition);
                methodDefinition.ReturnType = Utils.Utils.ImportTypeInto(methodDefinition, methodReturnType);

                if (tokenAttribute != null)
                {
                    CustomAttribute customTokenAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(tokenAttribute));
                    customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{methodDef.token:X}")));
                    methodDefinition.CustomAttributes.Add(customTokenAttribute);
                }

                if (methodDefinition.HasBody && ilTypeDefinition.BaseType?.FullName != "System.MulticastDelegate")
                    FillMethodBodyWithStub(methodDefinition);

                SharedState.MethodsByIndex.TryAdd(methodDef.MethodIndex, methodDefinition);
                SharedState.MethodsByAddress.TryAdd(methodDef.MethodPointer, methodDefinition);

                //Method Params
                HandleMethodParameters(methodDef, methodDefinition);

                var methodPointer = methodDef.MethodPointer;

                //Address attribute
                if ((methodPointer > 0 || methodDef.slot != ushort.MaxValue) && addressAttribute != null)
                {
                    var customAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(addressAttribute));
                    if (methodPointer > 0)
                    {
                        customAttribute.Fields.Add(new CustomAttributeNamedArgument("RVA", new CustomAttributeArgument(stringType, $"0x{LibCpp2IlMain.Binary.GetRVA(methodPointer):X}")));
                        customAttribute.Fields.Add(new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{LibCpp2IlMain.Binary.MapVirtualAddressToRaw(methodPointer):X}")));
                        customAttribute.Fields.Add(new CustomAttributeNamedArgument("VA", new CustomAttributeArgument(stringType, $"0x{methodPointer:X}")));
                    }

                    if (methodDef.slot != ushort.MaxValue)
                    {
                        customAttribute.Fields.Add(new CustomAttributeNamedArgument("Slot", new CustomAttributeArgument(stringType, methodDef.slot.ToString())));
                    }

                    methodDefinition.CustomAttributes.Add(customAttribute);
                }

                //Handle generic parameters.
                methodDef.GenericContainer?.GenericParameters.ToList()
                    .ForEach(p =>
                    {
                        if (SharedState.GenericParamsByIndex.TryGetValue(p.Index, out var gp))
                        {
                            if (!methodDefinition.GenericParameters.Contains(gp))
                                methodDefinition.GenericParameters.Add(gp);

                            return;
                        }

                        gp = new GenericParameter(p.Name, methodDefinition).WithFlags(p.flags);

                        if (!methodDefinition.GenericParameters.Contains(gp))
                            methodDefinition.GenericParameters.Add(gp);

                        p.ConstraintTypes!
                            .Select(c => new GenericParameterConstraint(Utils.Utils.ImportTypeInto(methodDefinition, c)))
                            .ToList()
                            .ForEach(gp.Constraints.Add);
                    });
                
                if (methodDef.slot < ushort.MaxValue)
                    SharedState.VirtualMethodsBySlot[methodDef.slot] = methodDefinition;
            }
        }

        private static void ProcessPropertiesInType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition, MethodDefinition? tokenAttribute, TypeReference stringType)
        {
            foreach (var propertyDef in cppTypeDefinition.Properties!)
            {
                var getter = propertyDef.Getter?.AsManaged();
                var setter = propertyDef.Setter?.AsManaged();

                var propertyType = getter?.ReturnType ?? setter?.Parameters[0]?.ParameterType;

                var propertyDefinition = new PropertyDefinition(propertyDef.Name, (PropertyAttributes) propertyDef.attrs, ilTypeDefinition.Module.ImportReference(propertyType))
                {
                    GetMethod = getter,
                    SetMethod = setter
                };

                if (tokenAttribute != null)
                {
                    var customTokenAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(tokenAttribute));
                    customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{propertyDef.token:X}")));
                    propertyDefinition.CustomAttributes.Add(customTokenAttribute);
                }

                ilTypeDefinition.Properties.Add(propertyDefinition);

                SharedState.UnmanagedToManagedProperties[propertyDef] = propertyDefinition;
                SharedState.ManagedToUnmanagedProperties[propertyDefinition] = propertyDef;
            }
        }

        private static void ProcessEventsInType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition, MethodDefinition? tokenAttribute, TypeReference stringType)
        {
            foreach (var il2cppEventDef in cppTypeDefinition.Events!)
            {
                var monoDef = new EventDefinition(il2cppEventDef.Name, (EventAttributes) il2cppEventDef.EventAttributes, Utils.Utils.ImportTypeInto(ilTypeDefinition, il2cppEventDef.RawType!))
                {
                    AddMethod = il2cppEventDef.Adder?.AsManaged(),
                    RemoveMethod = il2cppEventDef.Remover?.AsManaged(),
                    InvokeMethod = il2cppEventDef.Invoker?.AsManaged()
                };

                if (tokenAttribute != null)
                {
                    var customTokenAttribute = new CustomAttribute(ilTypeDefinition.Module.ImportReference(tokenAttribute));
                    customTokenAttribute.Fields.Add(new CustomAttributeNamedArgument("Token", new CustomAttributeArgument(stringType, $"0x{il2cppEventDef.token:X}")));
                    monoDef.CustomAttributes.Add(customTokenAttribute);
                }

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
                var parameterTypeRef = Utils.Utils.ImportTypeInto(monoMethodDef, il2cppParam.RawType);

                if (il2cppParam.RawType.byref == 1)
                    parameterTypeRef = new ByReferenceType(parameterTypeRef);

                ParameterDefinition monoParam = new ParameterDefinition(il2cppParam.ParameterName, (ParameterAttributes) il2cppParam.ParameterAttributes, parameterTypeRef);

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
                .Append($"\t\tAccessibility: {methodDef.Attributes & System.Reflection.MethodAttributes.MemberAccessMask}\n")
                .Append($"\t\tReturn Type: {methodDef.ReturnType}\n")
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