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

            var typeDefinition = typeCtx.GetExtraData<TypeDefinition>("AsmResolverType") ?? throw new($"AsmResolver type not found in type analysis context for {typeCtx.FullName}");

            //Type generic params.
            PopulateGenericParamsForType(typeCtx, typeDefinition);

            //Set base type
            if(asmCtx.AppContext.MetadataVersion >= 35 && typeCtx is {Definition.IsEnumType: true })
                //v35 restructures this a bit so that enums now directly inherit from their primitive type, so we need to explicitly set this to enum
                typeDefinition.BaseType = typeCtx.AppContext.SystemTypes.EnumType.ToTypeSignature(typeDefinition.DeclaringModule!).ToTypeDefOrRef();
            else
                typeDefinition.BaseType = typeCtx.BaseType?.ToTypeSignature(typeDefinition.DeclaringModule!).ToTypeDefOrRef();

            //Set interfaces
            foreach (var interfaceType in typeCtx.InterfaceContexts)
                typeDefinition.Interfaces.Add(new(interfaceType.ToTypeSignature(typeDefinition.DeclaringModule!).ToTypeDefOrRef()));
        }

        var assemblyDefinition = asmCtx.GetExtraData<AssemblyDefinition>("AsmResolverAssembly") ?? throw new("AsmResolver assembly not found in assembly analysis context for " + asmCtx);
        var moduleDefinition = assemblyDefinition.ManifestModule!;
        foreach (var typeCtx in asmCtx.ExportedTypes)
        {
            var owningAssembly = typeCtx.DeclaringAssembly.GetExtraData<AssemblyDefinition>("AsmResolverAssembly") ?? throw new("AsmResolver assembly not found in assembly analysis context for " + typeCtx.DeclaringAssembly);
            moduleDefinition.ExportedTypes.Add(new ExportedType(owningAssembly.ToAssemblyReference(), typeCtx.Namespace, typeCtx.Name));
        }
    }

    private static void PopulateGenericParamsForType(TypeAnalysisContext cppTypeDefinition, TypeDefinition ilTypeDefinition)
    {
        var importer = ilTypeDefinition.DeclaringModule!.DefaultImporter;

        foreach (var param in cppTypeDefinition.GenericParameters)
        {
            var p = new GenericParameter(param.Name, (GenericParameterAttributes)param.Attributes);

            ilTypeDefinition.GenericParameters.Add(p);

            param.ConstraintTypes
                .Select(c => new GenericParameterConstraint(c.ToTypeSignature(ilTypeDefinition.DeclaringModule).ToTypeDefOrRef()))
                .ToList()
                .ForEach(p.Constraints.Add);
        }
    }

    private static TypeSignature GetTypeSigFromAttributeArg(AssemblyDefinition parentAssembly, BaseCustomAttributeParameter parameter) =>
        parameter switch
        {
            CustomAttributePrimitiveParameter primitiveParameter => AsmResolverUtils.GetPrimitiveTypeDef(primitiveParameter.PrimitiveType).ToTypeSignature(),
            CustomAttributeEnumParameter enumParameter => enumParameter.EnumTypeContext.ToTypeSignature(parentAssembly.ManifestModule!),
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
                    CustomAttributeArrayParameter array => BuildArrayArgument(parentAssembly, array).Elements.ToArray(),
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

    /// <summary>
    /// Converts the given parameter to a custom attribute argument, given the context of the parent assembly.
    /// </summary>
    /// <param name="parentAssembly">The assembly that the resulting attribute will be part of.</param>
    /// <param name="parameter">The parameter to convert</param>
    /// <param name="boxIfNeeded">Whether the returned attribute will be used in context of a member that is typed as object. If true, the resulting attribute will be an object-typed one wrapping a BoxedArgument containing the real value. If false, the real value will be returned directly.</param>
    /// <remarks>
    /// BoxIfNeeded will cause the resulting attribute to be boxed if the parameter is an enum or a type parameter. This is required if, for example, the enum or type is being passed as the argument in a constructor for which the parameter is typed as object.
    /// </remarks>
    private static CustomAttributeArgument FromAnalyzedAttributeArgument(AssemblyDefinition parentAssembly, BaseCustomAttributeParameter parameter, bool boxIfNeeded)
    {
#if !DEBUG
        try
#endif
        {
            return parameter switch
            {
                CustomAttributePrimitiveParameter primitiveParameter when boxIfNeeded => new(TypeDefinitionsAsmResolver.Object.ToTypeSignature(), new BoxedArgument(GetTypeSigFromAttributeArg(parentAssembly, primitiveParameter), primitiveParameter.PrimitiveValue)),
                CustomAttributePrimitiveParameter primitiveParameter => new(GetTypeSigFromAttributeArg(parentAssembly, primitiveParameter), primitiveParameter.PrimitiveValue),
                
                CustomAttributeEnumParameter enumParameter when boxIfNeeded => new(TypeDefinitionsAsmResolver.Object.ToTypeSignature(), new BoxedArgument(GetTypeSigFromAttributeArg(parentAssembly, enumParameter), enumParameter.UnderlyingPrimitiveParameter.PrimitiveValue)),
                CustomAttributeEnumParameter enumParameter => new(GetTypeSigFromAttributeArg(parentAssembly, enumParameter), enumParameter.UnderlyingPrimitiveParameter.PrimitiveValue),
                
                //BaseCustomAttributeTypeParameter typeParameter when boxIfNeeded => new(TypeDefinitionsAsmResolver.Object.ToTypeSignature(), new BoxedArgument(GetTypeSigFromAttributeArg(parentAssembly, typeParameter), typeParameter.TypeContext?.ToTypeSignature(parentAssembly.ManifestModule!))),
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
        => new(CustomAttributeArgumentMemberType.Field, field.Field.Name, GetTypeSigFromAttributeArg(parentAssembly, field.Value), FromAnalyzedAttributeArgument(parentAssembly, field.Value, field.Field.FieldType == field.Field.AppContext.SystemTypes.SystemObjectType));

    private static CustomAttributeNamedArgument FromAnalyzedAttributeProperty(AssemblyDefinition parentAssembly, CustomAttributeProperty property)
        => new(CustomAttributeArgumentMemberType.Property, property.Property.Name, GetTypeSigFromAttributeArg(parentAssembly, property.Value), FromAnalyzedAttributeArgument(parentAssembly, property.Value, property.Property.PropertyType == property.Property.AppContext.SystemTypes.SystemObjectType));

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
                    signature = new(analyzedCustomAttribute.ConstructorParameters.Select(p => FromAnalyzedAttributeArgument(assemblyDefinition, p, analyzedCustomAttribute.Constructor.Parameters[p.Index].ParameterType == analyzedCustomAttribute.Constructor.AppContext.SystemTypes.SystemObjectType)));
                }
                else
                {
                    //Has named arguments.
                    signature = new(
                        analyzedCustomAttribute.ConstructorParameters.Select(p => FromAnalyzedAttributeArgument(assemblyDefinition, p, analyzedCustomAttribute.Constructor.Parameters[p.Index].ParameterType == analyzedCustomAttribute.Constructor.AppContext.SystemTypes.SystemObjectType)),
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

        var importedCtor = assemblyDefinition.ManifestModule!.DefaultImporter.ImportMethod(ctor);

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
            var assembly = asmContext.GetExtraData<AssemblyDefinition>("AsmResolverAssembly")!;
            CopyCustomAttributes(asmContext, assembly.CustomAttributes);
            CopyCustomAttributes(asmContext.ManifestModule, assembly.ManifestModule!.CustomAttributes);

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
                        CopyCustomAttributes(parameterAnalysisContext, parameterDefinitions[parameterAnalysisContext.ParameterIndex].CustomAttributes);
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
                throw new Exception($"Failed to process type {managedType.FullName} (module {managedType.DeclaringModule?.Name}, declaring type {managedType.DeclaringType?.FullName}) in {asmContext.Name}", e);
            }
#endif
        }
    }

    private static void CopyIl2CppDataToManagedType(TypeAnalysisContext typeContext, TypeDefinition ilTypeDefinition)
    {
        var importer = ilTypeDefinition.DeclaringModule!.DefaultImporter;

        CopyFieldsInType(importer, typeContext, ilTypeDefinition);

        CopyMethodsInType(importer, typeContext, ilTypeDefinition);

        CopyPropertiesInType(importer, typeContext, ilTypeDefinition);

        CopyEventsInType(importer, typeContext, ilTypeDefinition);
    }

    private static void CopyFieldsInType(ReferenceImporter importer, TypeAnalysisContext typeContext, TypeDefinition ilTypeDefinition)
    {
        foreach (var fieldContext in typeContext.Fields)
        {
            var fieldTypeSig = fieldContext.ToTypeSignature(importer.TargetModule);

            var managedField = new FieldDefinition(fieldContext.Name, (FieldAttributes)fieldContext.Attributes, fieldTypeSig);

            //Field default values
            if (managedField.HasDefault && fieldContext.ConstantValue is { } constVal)
                managedField.Constant = AsmResolverConstants.GetOrCreateConstant(constVal);

            //Field Initial Values (used for allocation of Array Literals)
            if (managedField.HasFieldRva)
                managedField.FieldRva = new DataSegment(fieldContext.StaticArrayInitialValue);

            //Copy field offset
            if (ilTypeDefinition.IsExplicitLayout && !fieldContext.IsStatic)
                managedField.FieldOffset = fieldContext.Offset;

            fieldContext.PutExtraData("AsmResolverField", managedField);

            ilTypeDefinition.Fields.Add(managedField);
        }
    }

    private static void CopyMethodsInType(ReferenceImporter importer, TypeAnalysisContext typeContext, TypeDefinition ilTypeDefinition)
    {
        foreach (var methodCtx in typeContext.Methods)
        {
            var returnType = methodCtx.ReturnType.ToTypeSignature(importer.TargetModule);

            var paramData = methodCtx.Parameters;
            var parameterTypes = new TypeSignature[paramData.Count];
            var parameterDefinitions = new ParameterDefinition[paramData.Count];
            foreach (var parameterAnalysisContext in methodCtx.Parameters)
            {
                var i = parameterAnalysisContext.ParameterIndex;
                parameterTypes[i] = parameterAnalysisContext.ParameterType.ToTypeSignature(importer.TargetModule);

                var sequence = (ushort)(i + 1); //Add one because sequence 0 is the return type
                parameterDefinitions[i] = new(sequence, parameterAnalysisContext.Name, (ParameterAttributes)parameterAnalysisContext.Attributes);

                if (parameterAnalysisContext.DefaultValue is not { } defaultValueData || !parameterAnalysisContext.Attributes.HasFlag(System.Reflection.ParameterAttributes.HasDefault))
                    continue;

                if (defaultValueData?.ContainedDefaultValue is { } constVal)
                    parameterDefinitions[i].Constant = AsmResolverConstants.GetOrCreateConstant(constVal);
                else if (defaultValueData is { dataIndex.IsNull: true })
                {
                    //Literal null
                    parameterDefinitions[i].Constant = AsmResolverConstants.Null;
                }
            }


            var signature = methodCtx.IsStatic
                ? MethodSignature.CreateStatic(returnType, methodCtx.GenericParameters.Count, parameterTypes)
                : MethodSignature.CreateInstance(returnType, methodCtx.GenericParameters.Count, parameterTypes);

            var managedMethod = new MethodDefinition(methodCtx.Name, (MethodAttributes)methodCtx.Attributes, signature);

            managedMethod.ImplAttributes = (MethodImplAttributes)methodCtx.ImplAttributes;

            if (methodCtx.Definition != null)
            {
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
            methodCtx.GenericParameters
                .ForEach(p =>
                {
                    var gp = new GenericParameter(p.Name, (GenericParameterAttributes)p.Attributes);

                    if (!managedMethod.GenericParameters.Contains(gp))
                        managedMethod.GenericParameters.Add(gp);

                    p.ConstraintTypes
                        .Select(c => new GenericParameterConstraint(c.ToTypeSignature(ilTypeDefinition.DeclaringModule!).ToTypeDefOrRef()))
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
            var propertyTypeSig = propertyCtx.ToTypeSignature(importer.TargetModule);
            var propertySignature = propertyCtx.IsStatic
                ? PropertySignature.CreateStatic(propertyTypeSig)
                : PropertySignature.CreateInstance(propertyTypeSig);

            var managedProperty = new PropertyDefinition(propertyCtx.Name, (PropertyAttributes)propertyCtx.Attributes, propertySignature);

            var managedGetter = propertyCtx.Getter?.GetExtraData<MethodDefinition>("AsmResolverMethod");
            var managedSetter = propertyCtx.Setter?.GetExtraData<MethodDefinition>("AsmResolverMethod");

            managedProperty.SetSemanticMethods(managedGetter, managedSetter);

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
            var eventType = eventCtx.ToTypeSignature(importer.TargetModule).ToTypeDefOrRef();

            var managedEvent = new EventDefinition(eventCtx.Name, (EventAttributes)eventCtx.Attributes, eventType);

            var managedAdder = eventCtx.Adder?.GetExtraData<MethodDefinition>("AsmResolverMethod");
            var managedRemover = eventCtx.Remover?.GetExtraData<MethodDefinition>("AsmResolverMethod");
            var managedInvoker = eventCtx.Invoker?.GetExtraData<MethodDefinition>("AsmResolverMethod");

            managedEvent.SetSemanticMethods(managedAdder, managedRemover, managedInvoker);

            eventCtx.PutExtraData("AsmResolverEvent", managedEvent);

            ilTypeDefinition.Events.Add(managedEvent);
        }
    }

    public static void AddExplicitInterfaceImplementations(AssemblyAnalysisContext asmContext)
    {
        var managedAssembly = asmContext.GetExtraData<AssemblyDefinition>("AsmResolverAssembly") ?? throw new("AsmResolver assembly not found in assembly analysis context for " + asmContext);
        var runtimeContext = asmContext.AppContext.GetExtraData<RuntimeContext>("AsmResolverRuntimeContext") ?? throw new("AsmResolver runtime context not found in application analysis context");

        var importer = managedAssembly.ManifestModule!.DefaultImporter;

        foreach (var typeContext in asmContext.Types)
        {
            if (IsTypeContextModule(typeContext))
                continue;

            var managedType = typeContext.GetExtraData<TypeDefinition>("AsmResolverType") ?? throw new($"AsmResolver type not found in type analysis context for {typeContext.Definition?.FullName}");

#if !DEBUG
            try
#endif
            {
                AddExplicitInterfaceImplementations(managedType, typeContext, importer, runtimeContext);
            }
#if !DEBUG
            catch (Exception e)
            {
                throw new Exception($"Failed to process type {managedType.FullName} (module {managedType.DeclaringModule?.Name}, declaring type {managedType.DeclaringType?.FullName}) in {asmContext.Name}", e);
            }
#endif
        }
    }

    private static void AddExplicitInterfaceImplementations(TypeDefinition type, TypeAnalysisContext typeContext, ReferenceImporter importer, RuntimeContext runtimeContext)
    {
        List<(PropertyDefinition InterfaceProperty, TypeSignature InterfaceType, MethodDefinition Method)>? getMethodsToCreate = null;
        List<(PropertyDefinition InterfaceProperty, TypeSignature InterfaceType, MethodDefinition Method)>? setMethodsToCreate = null;

        foreach (var methodContext in typeContext.Methods)
        {
            var isPrivate = (methodContext.Attributes & System.Reflection.MethodAttributes.MemberAccessMask) == System.Reflection.MethodAttributes.Private;

            foreach (var overrideContext in methodContext.Overrides)
            {
                if (overrideContext.Name == methodContext.Name && !isPrivate)
                    continue;

                var interfaceMethod = (IMethodDefOrRef)overrideContext.ToMethodDescriptor(importer.TargetModule);
                var method = methodContext.GetExtraData<MethodDefinition>("AsmResolverMethod") ?? throw new($"AsmResolver method not found in method analysis context for {methodContext}");
                type.MethodImplementations.Add(new MethodImplementation(interfaceMethod, method));
                var resolutionStatus = interfaceMethod.Resolve(runtimeContext, out var interfaceMethodResolved);
                if (resolutionStatus == ResolutionStatus.Success && interfaceMethodResolved != null)
                {
                    if (interfaceMethodResolved.IsGetMethod && !method.IsGetMethod)
                    {
                        getMethodsToCreate ??= [];
                        var interfacePropertyResolved = interfaceMethodResolved.DeclaringType!.Properties.First(p => p.Semantics.Contains(interfaceMethodResolved.Semantics));
                        getMethodsToCreate.Add((interfacePropertyResolved, interfaceMethod.DeclaringType!.ToTypeSignature(runtimeContext), method));
                    }
                    else if (interfaceMethodResolved.IsSetMethod && !method.IsSetMethod)
                    {
                        setMethodsToCreate ??= [];
                        var interfacePropertyResolved = interfaceMethodResolved.DeclaringType!.Properties.First(p => p.Semantics.Contains(interfaceMethodResolved.Semantics));
                        setMethodsToCreate.Add((interfacePropertyResolved, interfaceMethod.DeclaringType!.ToTypeSignature(runtimeContext), method));
                    }
                }
            }
        }

        // Il2Cpp doesn't include properties for explicit interface implementations, so we have to create them ourselves.
        if (getMethodsToCreate is not null)
        {
            foreach (var entry in getMethodsToCreate)
            {
                var (interfaceProperty, interfaceType, getMethod) = entry;
                var setMethod = setMethodsToCreate?
                    .FirstOrDefault(e => e.InterfaceProperty == interfaceProperty && runtimeContext.SignatureComparer.Equals(e.InterfaceType, interfaceType))
                    .Method;

                var name = $"{interfaceType.FullName}.{interfaceProperty.Name}";
                var propertySignature = getMethod.IsStatic
                    ? PropertySignature.CreateStatic(getMethod.Signature!.ReturnType, getMethod.Signature.ParameterTypes)
                    : PropertySignature.CreateInstance(getMethod.Signature!.ReturnType, getMethod.Signature.ParameterTypes);
                var property = new PropertyDefinition(name, interfaceProperty.Attributes, propertySignature);
                type.Properties.Add(property);
                property.SetSemanticMethods(getMethod, setMethod);
            }
        }
        if (setMethodsToCreate is not null)
        {
            foreach (var entry in setMethodsToCreate)
            {
                var (interfaceProperty, interfaceType, setMethod) = entry;
                if (getMethodsToCreate?.Any(e => e.InterfaceProperty == interfaceProperty && runtimeContext.SignatureComparer.Equals(e.InterfaceType, interfaceType)) == true)
                    continue;
                var name = $"{interfaceType.FullName}.{interfaceProperty.Name}";
                var propertySignature = setMethod.IsStatic
                    ? PropertySignature.CreateStatic(setMethod.Signature!.ParameterTypes[^1], setMethod.Signature.ParameterTypes.Take(setMethod.Signature.ParameterTypes.Count - 1))
                    : PropertySignature.CreateInstance(setMethod.Signature!.ParameterTypes[^1], setMethod.Signature.ParameterTypes.Take(setMethod.Signature.ParameterTypes.Count - 1));
                var property = new PropertyDefinition(name, interfaceProperty.Attributes, propertySignature);
                type.Properties.Add(property);
                property.SetSemanticMethods(null, setMethod);
            }
        }
    }
}
