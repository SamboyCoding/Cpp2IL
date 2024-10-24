using System;
using System.Collections.Generic;
using System.Linq;
using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Metadata.Tables;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Model.CustomAttributes;
using LibCpp2IL.BinaryStructures;
using LibCpp2IL.Metadata;
using LibCpp2IL.Reflection;

namespace Cpp2IL.Core.Utils.AsmResolver;

public static class AsmResolverAssemblyPopulator
{
    public static bool IsTypeContextModule(TypeAnalysisContext typeCtx)
    {
        return typeCtx.Name.StartsWith("<Module>") || typeCtx.FullName.StartsWith("<Module>");
    }

    public static void ConfigureHierarchy(AssemblyAnalysisContext asmCtx)
    {
        foreach (var typeCtx in asmCtx.Types)
        {
            if (IsTypeContextModule(typeCtx))
                continue;

            var il2CppTypeDef = typeCtx.Definition;
            var typeDefinition = typeCtx.GetExtraData<TypeDefinition>("AsmResolverType") ?? throw new($"AsmResolver type not found in type analysis context for {typeCtx.FullName}");

            var importer = typeDefinition.Module!.Assembly!.GetImporter();

            //Type generic params.
            if (il2CppTypeDef != null)
                PopulateGenericParamsForType(il2CppTypeDef, typeDefinition);

            //Set base type
            if (typeCtx.OverrideBaseType is { } overrideBaseType)
            {
                var baseTypeDef = overrideBaseType.GetExtraData<TypeDefinition>("AsmResolverType") ?? throw new($"{typeCtx} declares override base type {overrideBaseType} which has not had an AsmResolver type generated for it.");
                typeDefinition.BaseType = importer.ImportType(baseTypeDef);
            }
            else if (il2CppTypeDef?.RawBaseType is { } parent)
                typeDefinition.BaseType = importer.ImportType(AsmResolverUtils.ImportReferenceFromIl2CppType(typeDefinition.Module, parent));

            //Set interfaces
            if (il2CppTypeDef != null)
                foreach (var interfaceType in il2CppTypeDef.RawInterfaces)
                    typeDefinition.Interfaces.Add(new(importer.ImportType(AsmResolverUtils.ImportReferenceFromIl2CppType(typeDefinition.Module, interfaceType))));
        }
    }

    private static void PopulateGenericParamsForType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition)
    {
        if (cppTypeDefinition.GenericContainer == null)
            return;

        var importer = ilTypeDefinition.Module!.Assembly!.GetImporter();

        foreach (var param in cppTypeDefinition.GenericContainer.GenericParameters)
        {
            // if(parentParams.Any(p => p.Name == param.Name))
            //     continue;

            if (!AsmResolverUtils.GenericParamsByIndexNew.TryGetValue(param.Index, out var p))
            {
                p = new GenericParameter(param.Name, (GenericParameterAttributes)param.flags);
                AsmResolverUtils.GenericParamsByIndexNew[param.Index] = p;

                ilTypeDefinition.GenericParameters.Add(p);

                param.ConstraintTypes!
                    .Select(c => new GenericParameterConstraint(importer.ImportTypeIfNeeded(AsmResolverUtils.ImportReferenceFromIl2CppType(ilTypeDefinition.Module, c))))
                    .ToList()
                    .ForEach(p.Constraints.Add);
            }
            else if (!ilTypeDefinition.GenericParameters.Contains(p))
                ilTypeDefinition.GenericParameters.Add(p);
        }
    }

    private static TypeSignature GetTypeSigFromAttributeArg(AssemblyDefinition parentAssembly, BaseCustomAttributeParameter parameter) =>
        parameter switch
        {
            CustomAttributePrimitiveParameter primitiveParameter => AsmResolverUtils.GetPrimitiveTypeDef(primitiveParameter.PrimitiveType).ToTypeSignature(),
            CustomAttributeEnumParameter enumParameter => AsmResolverUtils.GetTypeSignatureFromIl2CppType(parentAssembly.ManifestModule!, enumParameter.EnumType ?? throw new("Enum type not found for " + enumParameter)),
            BaseCustomAttributeTypeParameter => TypeDefinitionsAsmResolver.Type.ToTypeSignature(),
            CustomAttributeArrayParameter arrayParameter => AsmResolverUtils.GetPrimitiveTypeDef(arrayParameter.ArrType).ToTypeSignature().MakeSzArrayType(),
            _ => throw new ArgumentException("Unknown custom attribute parameter type: " + parameter.GetType().FullName)
        };

    private static CustomAttributeArgument BuildArrayArgument(AssemblyDefinition parentAssembly, CustomAttributeArrayParameter arrayParameter)
    {
#if !DEBUG
        try
#endif
        {
            if (arrayParameter.IsNullArray)
                return BuildEmptyArrayArgument(parentAssembly, arrayParameter);

            var typeSig = GetTypeSigFromAttributeArg(parentAssembly, arrayParameter);

            var isObjectArray = arrayParameter.ArrType == Il2CppTypeEnum.IL2CPP_TYPE_OBJECT;

            var arrayElements = arrayParameter.ArrayElements.Select(e =>
            {
                var rawValue = e switch
                {
                    CustomAttributePrimitiveParameter primitiveParameter => primitiveParameter.PrimitiveValue,
                    CustomAttributeEnumParameter enumParameter => enumParameter.UnderlyingPrimitiveParameter.PrimitiveValue,
                    BaseCustomAttributeTypeParameter type => (object?)type.TypeContext?.ToTypeSignature(parentAssembly.ManifestModule!),
                    CustomAttributeNullParameter => null,
                    _ => throw new("Not supported array element type: " + e.GetType().FullName)
                };

                if (isObjectArray)
                    //Object params have to be boxed
                    return new BoxedArgument(GetTypeSigFromAttributeArg(parentAssembly, e), rawValue);

                return rawValue;
            }).ToArray();

            return new(typeSig, arrayElements);
        }
#if !DEBUG
        catch (Exception e)
        {
            throw new("Failed to build array argument for " + arrayParameter, e);
        }
#endif
    }

    private static CustomAttributeArgument BuildEmptyArrayArgument(AssemblyDefinition parentAssembly, CustomAttributeArrayParameter arrayParameter)
    {
        //Need to resolve the type of the array because it's not in the blob and AsmResolver needs it.

        var typeSig = arrayParameter.Kind switch
        {
            CustomAttributeParameterKind.ConstructorParam => arrayParameter.Owner.Constructor.Parameters[arrayParameter.Index].ToTypeSignature(parentAssembly.ManifestModule!),
            CustomAttributeParameterKind.Property => arrayParameter.Owner.Properties[arrayParameter.Index].Property.ToTypeSignature(parentAssembly.ManifestModule!),
            CustomAttributeParameterKind.Field => arrayParameter.Owner.Fields[arrayParameter.Index].Field.ToTypeSignature(parentAssembly.ManifestModule!),
            CustomAttributeParameterKind.ArrayElement => throw new("Array element cannot be an array (or at least, not implemented!)"),
            _ => throw new("Unknown array parameter kind: " + arrayParameter.Kind)
        };

        return new(typeSig) { IsNullArray = true };
    }

    private static CustomAttributeArgument FromAnalyzedAttributeArgument(AssemblyDefinition parentAssembly, BaseCustomAttributeParameter parameter)
    {
#if !DEBUG
        try
#endif
        {
            return parameter switch
            {
                CustomAttributePrimitiveParameter primitiveParameter => new(GetTypeSigFromAttributeArg(parentAssembly, primitiveParameter), primitiveParameter.PrimitiveValue),
                CustomAttributeEnumParameter enumParameter => new(GetTypeSigFromAttributeArg(parentAssembly, enumParameter), enumParameter.UnderlyingPrimitiveParameter.PrimitiveValue),
                BaseCustomAttributeTypeParameter typeParameter => new(TypeDefinitionsAsmResolver.Type.ToTypeSignature(), typeParameter.TypeContext?.ToTypeSignature(parentAssembly.ManifestModule!)),
                CustomAttributeArrayParameter arrayParameter => BuildArrayArgument(parentAssembly, arrayParameter),
                _ => throw new ArgumentException("Unknown custom attribute parameter type: " + parameter.GetType().FullName)
            };
        }
#if !DEBUG
        catch (Exception e)
        {
            throw new("Failed to build custom attribute argument for " + parameter, e);
        }
#endif
    }

    private static CustomAttributeNamedArgument FromAnalyzedAttributeField(AssemblyDefinition parentAssembly, CustomAttributeField field)
        => new(CustomAttributeArgumentMemberType.Field, field.Field.FieldName, GetTypeSigFromAttributeArg(parentAssembly, field.Value), FromAnalyzedAttributeArgument(parentAssembly, field.Value));

    private static CustomAttributeNamedArgument FromAnalyzedAttributeProperty(AssemblyDefinition parentAssembly, CustomAttributeProperty property)
        => new(CustomAttributeArgumentMemberType.Property, property.Property.Name, GetTypeSigFromAttributeArg(parentAssembly, property.Value), FromAnalyzedAttributeArgument(parentAssembly, property.Value));

    private static CustomAttribute? ConvertCustomAttribute(AnalyzedCustomAttribute analyzedCustomAttribute, AssemblyDefinition assemblyDefinition)
    {
        var ctor = analyzedCustomAttribute.Constructor.GetExtraData<MethodDefinition>("AsmResolverMethod") ?? throw new($"Found a custom attribute with no AsmResolver constructor: {analyzedCustomAttribute}");

        CustomAttributeSignature signature;
        var numNamedArgs = analyzedCustomAttribute.Fields.Count + analyzedCustomAttribute.Properties.Count;

#if !DEBUG
        try
#endif
        {
            if (!analyzedCustomAttribute.HasAnyParameters && numNamedArgs == 0)
                signature = new();
            else if (analyzedCustomAttribute.IsSuitableForEmission)
            {
                if (numNamedArgs == 0)
                {
                    //Only fixed arguments.
                    signature = new(analyzedCustomAttribute.ConstructorParameters.Select(p => FromAnalyzedAttributeArgument(assemblyDefinition, p)));
                }
                else
                {
                    //Has named arguments.
                    signature = new(
                        analyzedCustomAttribute.ConstructorParameters.Select(p => FromAnalyzedAttributeArgument(assemblyDefinition, p)),
                        analyzedCustomAttribute.Fields
                            .Select(f => FromAnalyzedAttributeField(assemblyDefinition, f))
                            .Concat(analyzedCustomAttribute.Properties.Select(p => FromAnalyzedAttributeProperty(assemblyDefinition, p)))
                    );
                }
            }
            else
            {
                return null;
            }
        }
#if !DEBUG
        catch (Exception e)
        {
            throw new("Failed to build custom attribute signature for " + analyzedCustomAttribute, e);
        }
#endif

        var importedCtor = assemblyDefinition.GetImporter().ImportMethod(ctor);

        var newAttribute = new CustomAttribute((ICustomAttributeType)importedCtor, signature);
        return newAttribute;
    }

    private static void CopyCustomAttributes(HasCustomAttributes source, IList<CustomAttribute> destination)
    {
        if (source.CustomAttributes == null)
            return;

        var assemblyDefinition = source.CustomAttributeAssembly.GetExtraData<AssemblyDefinition>("AsmResolverAssembly") ?? throw new("AsmResolver assembly not found in assembly analysis context for " + source.CustomAttributeAssembly);

#if !DEBUG
        try
#endif
        {
            foreach (var analyzedCustomAttribute in source.CustomAttributes)
            {
                var asmResolverCustomAttribute = ConvertCustomAttribute(analyzedCustomAttribute, assemblyDefinition);
                if (asmResolverCustomAttribute != null)
                    destination.Add(asmResolverCustomAttribute);
            }
        }
#if !DEBUG
        catch (Exception e)
        {
            throw new("Failed to copy custom attributes for " + source, e);
        }
#endif
    }

    public static void PopulateCustomAttributes(AssemblyAnalysisContext asmContext)
    {
#if !DEBUG
        try
#endif
        {
            CopyCustomAttributes(asmContext, asmContext.GetExtraData<AssemblyDefinition>("AsmResolverAssembly")!.CustomAttributes);

            foreach (var type in asmContext.Types)
            {
                if (IsTypeContextModule(type))
                    continue;

                CopyCustomAttributes(type, type.GetExtraData<TypeDefinition>("AsmResolverType")!.CustomAttributes);

                foreach (var method in type.Methods)
                {
                    var methodDef = method.GetExtraData<MethodDefinition>("AsmResolverMethod")!;
                    CopyCustomAttributes(method, methodDef.CustomAttributes);

                    var parameterDefinitions = methodDef.ParameterDefinitions;
                    foreach (var parameterAnalysisContext in method.Parameters)
                    {
                        CopyCustomAttributes(parameterAnalysisContext, parameterDefinitions[parameterAnalysisContext.ParamIndex].CustomAttributes);
                    }
                }

                foreach (var field in type.Fields)
                    CopyCustomAttributes(field, field.GetExtraData<FieldDefinition>("AsmResolverField")!.CustomAttributes);

                foreach (var property in type.Properties)
                    CopyCustomAttributes(property, property.GetExtraData<PropertyDefinition>("AsmResolverProperty")!.CustomAttributes);

                foreach (var eventDefinition in type.Events)
                    CopyCustomAttributes(eventDefinition, eventDefinition.GetExtraData<EventDefinition>("AsmResolverEvent")!.CustomAttributes);
            }
        }
#if !DEBUG
        catch (Exception e)
        {
            throw new($"Failed to populate custom attributes in {asmContext}", e);
        }
#endif
    }

    public static void CopyDataFromIl2CppToManaged(AssemblyAnalysisContext asmContext)
    {
        var managedAssembly = asmContext.GetExtraData<AssemblyDefinition>("AsmResolverAssembly") ?? throw new("AsmResolver assembly not found in assembly analysis context for " + asmContext);

        foreach (var typeContext in asmContext.Types)
        {
            if (IsTypeContextModule(typeContext))
                continue;

            var managedType = typeContext.GetExtraData<TypeDefinition>("AsmResolverType") ?? throw new($"AsmResolver type not found in type analysis context for {typeContext.Definition?.FullName}");
            // CopyCustomAttributes(typeContext, managedType.CustomAttributes);

#if !DEBUG
            try
#endif
            {
                CopyIl2CppDataToManagedType(typeContext, managedType);
            }
#if !DEBUG
            catch (Exception e)
            {
                throw new Exception($"Failed to process type {managedType.FullName} (module {managedType.Module?.Name}, declaring type {managedType.DeclaringType?.FullName}) in {asmContext.Definition.AssemblyName.Name}", e);
            }
#endif
        }
    }

    private static void CopyIl2CppDataToManagedType(TypeAnalysisContext typeContext, TypeDefinition ilTypeDefinition)
    {
        var importer = ilTypeDefinition.Module!.Assembly!.GetImporter();

        CopyFieldsInType(importer, typeContext, ilTypeDefinition);

        CopyMethodsInType(importer, typeContext, ilTypeDefinition);

        CopyPropertiesInType(importer, typeContext, ilTypeDefinition);

        CopyEventsInType(importer, typeContext, ilTypeDefinition);
    }

    private static void CopyFieldsInType(ReferenceImporter importer, TypeAnalysisContext typeContext, TypeDefinition ilTypeDefinition)
    {
        foreach (var fieldContext in typeContext.Fields)
        {
            var fieldInfo = fieldContext.BackingData;

            var fieldTypeSig = importer.ImportTypeSignature(fieldContext.ToTypeSignature(importer.TargetModule));

            var managedField = new FieldDefinition(fieldContext.FieldName, (FieldAttributes)fieldContext.Attributes, fieldTypeSig);

            if (fieldInfo != null)
            {
                //Field default values
                if (managedField.HasDefault && fieldInfo.Field.DefaultValue?.Value is { } constVal)
                    managedField.Constant = AsmResolverConstants.GetOrCreateConstant(constVal);

                //Field Initial Values (used for allocation of Array Literals)
                if (managedField.HasFieldRva)
                    managedField.FieldRva = new DataSegment(fieldInfo.Field.StaticArrayInitialValue);

                if (ilTypeDefinition.IsExplicitLayout)
                    //Copy field offset
                    managedField.FieldOffset = fieldInfo.FieldOffset;
            }

            fieldContext.PutExtraData("AsmResolverField", managedField);

            ilTypeDefinition.Fields.Add(managedField);
        }
    }

    private static void CopyMethodsInType(ReferenceImporter importer, TypeAnalysisContext typeContext, TypeDefinition ilTypeDefinition)
    {
        foreach (var methodCtx in typeContext.Methods)
        {
            var methodDef = methodCtx.Definition;

            var rawReturnType = methodDef != null ? methodDef.RawReturnType! : LibCpp2IlReflection.GetTypeFromDefinition(methodCtx.InjectedReturnType!.Definition ?? throw new("Injected methods with injected return types not supported at the moment."))!;
            var returnType = importer.ImportTypeSignature(AsmResolverUtils.GetTypeSignatureFromIl2CppType(importer.TargetModule, rawReturnType));

            var paramData = methodCtx.Parameters;
            var parameterTypes = new TypeSignature[paramData.Count];
            var parameterDefinitions = new ParameterDefinition[paramData.Count];
            foreach (var parameterAnalysisContext in methodCtx.Parameters)
            {
                var i = parameterAnalysisContext.ParamIndex;
                parameterTypes[i] = importer.ImportTypeSignature(parameterAnalysisContext.ParameterTypeContext.ToTypeSignature(importer.TargetModule));

                var sequence = (ushort)(i + 1); //Add one because sequence 0 is the return type
                parameterDefinitions[i] = new(sequence, parameterAnalysisContext.Name, (ParameterAttributes)parameterAnalysisContext.ParameterAttributes);

                if (parameterAnalysisContext.DefaultValue is not { } defaultValueData)
                    continue;

                if (defaultValueData?.ContainedDefaultValue is { } constVal)
                    parameterDefinitions[i].Constant = AsmResolverConstants.GetOrCreateConstant(constVal);
                else if (defaultValueData is { dataIndex: -1 })
                {
                    //Literal null
                    parameterDefinitions[i].Constant = AsmResolverConstants.Null;
                }
            }


            var signature = methodCtx.IsStatic ? MethodSignature.CreateStatic(returnType, parameterTypes) : MethodSignature.CreateInstance(returnType, parameterTypes);

            var managedMethod = new MethodDefinition(methodCtx.Name, (MethodAttributes)methodCtx.Attributes, signature);

            if (methodCtx.Definition != null)
            {
                managedMethod.ImplAttributes = (MethodImplAttributes)methodCtx.Definition.MethodImplAttributes;
                if (methodCtx.Definition.IsUnmanagedCallersOnly && typeContext.AppContext.SystemTypes.UnmanagedCallersOnlyAttributeType != null)
                {
                    var unmanagedCallersOnlyType = typeContext.AppContext.SystemTypes.UnmanagedCallersOnlyAttributeType.GetExtraData<TypeDefinition>("AsmResolverType");
                    if(unmanagedCallersOnlyType != null)
                        managedMethod.CustomAttributes.Add(new CustomAttribute((ICustomAttributeType)importer.ImportMethod(unmanagedCallersOnlyType.GetConstructor()!), new()));
                }

            }

            //Add parameter definitions if we have them so we get names, defaults, out params, etc
            foreach (var parameterDefinition in parameterDefinitions)
            {
                managedMethod.ParameterDefinitions.Add(parameterDefinition);
            }

            //Handle generic parameters.
            methodDef?.GenericContainer?.GenericParameters.ToList()
                .ForEach(p =>
                {
                    if (AsmResolverUtils.GenericParamsByIndexNew.TryGetValue(p.Index, out var gp))
                    {
                        if (!managedMethod.GenericParameters.Contains(gp))
                            managedMethod.GenericParameters.Add(gp);

                        return;
                    }

                    gp = new(p.Name, (GenericParameterAttributes)p.flags);

                    if (!managedMethod.GenericParameters.Contains(gp))
                        managedMethod.GenericParameters.Add(gp);

                    p.ConstraintTypes!
                        .Select(c => new GenericParameterConstraint(importer.ImportTypeIfNeeded(AsmResolverUtils.ImportReferenceFromIl2CppType(ilTypeDefinition.Module!, c))))
                        .ToList()
                        .ForEach(gp.Constraints.Add);
                });


            methodCtx.PutExtraData("AsmResolverMethod", managedMethod);
            ilTypeDefinition.Methods.Add(managedMethod);
        }
    }

    private static void CopyPropertiesInType(ReferenceImporter importer, TypeAnalysisContext typeContext, TypeDefinition ilTypeDefinition)
    {
        foreach (var propertyCtx in typeContext.Properties)
        {
            var propertyDef = propertyCtx.Definition;

            var propertyTypeSig = importer.ImportTypeSignature(AsmResolverUtils.GetTypeSignatureFromIl2CppType(importer.TargetModule, propertyDef.RawPropertyType!));
            var propertySignature = propertyDef.IsStatic
                ? PropertySignature.CreateStatic(propertyTypeSig)
                : PropertySignature.CreateInstance(propertyTypeSig);

            var managedProperty = new PropertyDefinition(propertyCtx.Name, (PropertyAttributes)propertyDef.attrs, propertySignature);

            var managedGetter = propertyCtx.Getter?.GetExtraData<MethodDefinition>("AsmResolverMethod");
            var managedSetter = propertyCtx.Setter?.GetExtraData<MethodDefinition>("AsmResolverMethod");

            if (managedGetter != null)
                managedProperty.Semantics.Add(new(managedGetter, MethodSemanticsAttributes.Getter));

            if (managedSetter != null)
                managedProperty.Semantics.Add(new(managedSetter, MethodSemanticsAttributes.Setter));

            //Indexer parameters
            if (managedGetter != null && managedGetter.Parameters.Count > 0)
            {
                foreach (var parameter in managedGetter.Parameters)
                {
                    propertySignature.ParameterTypes.Add(parameter.ParameterType);
                }
            }
            else if (managedSetter != null && managedSetter.Parameters.Count > 1)
            {
                //value parameter is always last
                for (var i = 0; i < managedSetter.Parameters.Count - 1; i++)
                {
                    var parameter = managedSetter.Parameters[i];
                    propertySignature.ParameterTypes.Add(parameter.ParameterType);
                }
            }

            propertyCtx.PutExtraData("AsmResolverProperty", managedProperty);

            ilTypeDefinition.Properties.Add(managedProperty);
        }
    }

    private static void CopyEventsInType(ReferenceImporter importer, TypeAnalysisContext cppTypeDefinition, TypeDefinition ilTypeDefinition)
    {
        foreach (var eventCtx in cppTypeDefinition.Events)
        {
            var eventDef = eventCtx.Definition;

            var eventType = importer.ImportTypeIfNeeded(AsmResolverUtils.ImportReferenceFromIl2CppType(ilTypeDefinition.Module!, eventDef.RawType!));

            var managedEvent = new EventDefinition(eventCtx.Name, (EventAttributes)eventDef.EventAttributes, eventType);

            var managedAdder = eventCtx.Adder?.GetExtraData<MethodDefinition>("AsmResolverMethod");
            var managedRemover = eventCtx.Remover?.GetExtraData<MethodDefinition>("AsmResolverMethod");
            var managedInvoker = eventCtx.Invoker?.GetExtraData<MethodDefinition>("AsmResolverMethod");

            if (managedAdder != null)
                managedEvent.Semantics.Add(new(managedAdder, MethodSemanticsAttributes.AddOn));

            if (managedRemover != null)
                managedEvent.Semantics.Add(new(managedRemover, MethodSemanticsAttributes.RemoveOn));

            if (managedInvoker != null)
                managedEvent.Semantics.Add(new(managedInvoker, MethodSemanticsAttributes.Fire));

            eventCtx.PutExtraData("AsmResolverEvent", managedEvent);

            ilTypeDefinition.Events.Add(managedEvent);
        }
    }

    public static void InferExplicitInterfaceImplementations(AssemblyAnalysisContext asmContext)
    {
        var managedAssembly = asmContext.GetExtraData<AssemblyDefinition>("AsmResolverAssembly") ?? throw new("AsmResolver assembly not found in assembly analysis context for " + asmContext);

        var importer = managedAssembly.GetImporter();

        foreach (var typeContext in asmContext.Types)
        {
            if (IsTypeContextModule(typeContext))
                continue;

            var managedType = typeContext.GetExtraData<TypeDefinition>("AsmResolverType") ?? throw new($"AsmResolver type not found in type analysis context for {typeContext.Definition?.FullName}");

#if !DEBUG
            try
#endif
            {
                InferExplicitInterfaceImplementations(managedType, importer);
            }
#if !DEBUG
            catch (Exception e)
            {
                throw new Exception($"Failed to process type {managedType.FullName} (module {managedType.Module?.Name}, declaring type {managedType.DeclaringType?.FullName}) in {asmContext.Definition.AssemblyName.Name}", e);
            }
#endif
        }
    }

    private static void InferExplicitInterfaceImplementations(TypeDefinition type, ReferenceImporter importer)
    {
        foreach (var method in type.Methods)
        {
            if (Utf8String.IsNullOrEmpty(method.Name))
                continue;

            // Explicit interface implementation
            // Note: This does not handle all cases.
            // Specifically, it does not handle the case where the interface has multiple methods with the same name.
            var periodLastIndex = method.Name.LastIndexOf('.');
            if (periodLastIndex < 0 || !method.IsPrivate || !method.IsVirtual || !method.IsFinal || !method.IsNewSlot)
            {
                continue;
            }

            var methodName = method.Name.Value[(periodLastIndex + 1)..];
            var interfaceName = method.Name.Value[..periodLastIndex];
            var genericParameterNames = type.GenericParameters.Count > 0
                ? type.GenericParameters.Select(p => (string?)p.Name ?? "").ToArray()
                : [];
            var interfaceType = AsmResolverUtils.TryLookupTypeSignatureByName(interfaceName, genericParameterNames);

            IMethodDefOrRef? interfaceMethod = null;
            var underlyingInterface = interfaceType?.GetUnderlyingTypeDefOrRef();
            foreach (var interfaceMethodDef in (underlyingInterface as TypeDefinition)?.Methods ?? [])
            {
                if (interfaceMethodDef.Name != methodName)
                    continue;

                if (interfaceMethod is not null)
                {
                    // Ambiguity. Checking the method signature would be required to disambiguate.
                    interfaceMethod = null;
                    break;
                }

                interfaceMethod = new MemberReference(interfaceType?.ToTypeDefOrRef(), interfaceMethodDef.Name, interfaceMethodDef.Signature);
            }

            if (interfaceMethod != null)
            {
                type.MethodImplementations.Add(new MethodImplementation(importer.ImportMethod(interfaceMethod), method));
            }
        }
    }

}
