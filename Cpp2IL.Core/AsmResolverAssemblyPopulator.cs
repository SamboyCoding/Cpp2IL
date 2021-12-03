using System;
using System.Linq;
using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;
using Cpp2IL.Core.Utils;
using LibCpp2IL.Metadata;

namespace Cpp2IL.Core;

public static class AsmResolverAssemblyPopulator
{
    public static void ConfigureHierarchy()
    {
        foreach (var typeDefinition in SharedState.AllTypeDefinitionsNew)
        {
            var il2CppTypeDef = SharedState.ManagedToUnmanagedTypesNew[typeDefinition];
            var importer = typeDefinition.Module!.Assembly!.GetImporter();

            //Type generic params.
            PopulateGenericParamsForType(il2CppTypeDef, typeDefinition);

            //Set base type
            if (il2CppTypeDef.RawBaseType is { } parent)
                typeDefinition.BaseType = importer.ImportType(AsmResolverUtils.GetTypeDefFromIl2CppType(importer, parent).ToTypeDefOrRef());

            //Set interfaces
            foreach (var interfaceType in il2CppTypeDef.RawInterfaces)
                typeDefinition.Interfaces.Add(new(importer.ImportType(AsmResolverUtils.GetTypeDefFromIl2CppType(importer, interfaceType).ToTypeDefOrRef())));
        }
    }

    private static void PopulateGenericParamsForType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition)
    {
        if (cppTypeDefinition.GenericContainer == null)
            return;

        var importer = ilTypeDefinition.Module!.Assembly!.GetImporter();

        foreach (var param in cppTypeDefinition.GenericContainer.GenericParameters)
        {
            if (!SharedState.GenericParamsByIndexNew.TryGetValue(param.Index, out var p))
            {
                p = new GenericParameter(param.Name, (GenericParameterAttributes) param.flags);
                SharedState.GenericParamsByIndexNew[param.Index] = p;

                ilTypeDefinition.GenericParameters.Add(p);

                param.ConstraintTypes!
                    .Select(c => new GenericParameterConstraint(importer.ImportTypeIfNeeded(AsmResolverUtils.GetTypeDefFromIl2CppType(importer, c).ToTypeDefOrRef())))
                    .ToList()
                    .ForEach(p.Constraints.Add);
            }
            else if (!ilTypeDefinition.GenericParameters.Contains(p))
                ilTypeDefinition.GenericParameters.Add(p);
        }
    }

    public static void CopyDataFromIl2CppToManaged(Il2CppImageDefinition imageDef)
    {
        foreach (var il2CppTypeDefinition in imageDef.Types!)
        {
            if (il2CppTypeDefinition.Name == "<Module>")
                continue;

            var managedType = SharedState.UnmanagedToManagedTypesNew[il2CppTypeDefinition];

#if !DEBUG
            try
            {
#endif
            CopyIl2CppDataToManagedType(il2CppTypeDefinition, managedType);
#if !DEBUG
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to process type {managedType.FullName} (module {managedType.Module?.Name}, declaring type {managedType.DeclaringType?.FullName}) in {imageDef.Name}", e);
            }
#endif
        }
    }

    private static void CopyIl2CppDataToManagedType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition)
    {
        var importer = ilTypeDefinition.Module!.Assembly!.GetImporter();

        CopyFieldsInType(importer, cppTypeDefinition, ilTypeDefinition);

        CopyMethodsInType(importer, cppTypeDefinition, ilTypeDefinition);

        CopyPropertiesInType(importer, cppTypeDefinition, ilTypeDefinition);

        CopyEventsInType(importer, cppTypeDefinition, ilTypeDefinition);
    }

    private static void CopyFieldsInType(ReferenceImporter importer, Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition)
    {
        foreach (var fieldInfo in cppTypeDefinition.FieldInfos!)
        {
            var fieldTypeSig = importer.ImportTypeSignature(AsmResolverUtils.GetTypeDefFromIl2CppType(importer, fieldInfo.field.RawFieldType!).ToTypeSignature());
            var fieldSignature = (fieldInfo.attributes & System.Reflection.FieldAttributes.Static) != 0
                ? FieldSignature.CreateStatic(fieldTypeSig)
                : FieldSignature.CreateInstance(fieldTypeSig);

            var managedField = new FieldDefinition(fieldInfo.field.Name, (FieldAttributes) fieldInfo.attributes, fieldSignature);

            //Field default values
            if (managedField.HasDefault && fieldInfo.field.DefaultValue?.Value is { } constVal)
                managedField.Constant = AsmResolverUtils.MakeConstant(constVal);

            //Field Initial Values (used for allocation of Array Literals)
            if (managedField.HasFieldRva)
                managedField.FieldRva = new DataSegment(fieldInfo.field.StaticArrayInitialValue);

            ilTypeDefinition.Fields.Add(managedField);
        }
    }

    private static void CopyMethodsInType(ReferenceImporter importer, Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition)
    {
        foreach (var method in cppTypeDefinition.Methods!)
        {
            var returnType = importer.ImportTypeSignature(AsmResolverUtils.GetTypeDefFromIl2CppType(importer, method.RawReturnType!).ToTypeSignature());
            var parameterTypes = method.InternalParameterData!
                .Select(p => AsmResolverUtils.GetTypeDefFromIl2CppType(importer, p.RawType!).ToTypeSignature())
                .Select(importer.ImportTypeSignature)
                .ToArray();

            var signature = method.IsStatic ? MethodSignature.CreateStatic(returnType, parameterTypes) : MethodSignature.CreateInstance(returnType, parameterTypes);

            var managedMethod = new MethodDefinition(method.Name, (MethodAttributes) method.Attributes, signature);

            //Add parameter definitions so we get names, defaults, out params, etc
            var paramData = method.Parameters!;
            ushort seq = 1;
            foreach (var param in paramData)
            {
                var managedParam = new ParameterDefinition(seq++, param.ParameterName, (ParameterAttributes) param.ParameterAttributes);
                if (managedParam.HasDefault && param.DefaultValue is { } defaultValue)
                    managedParam.Constant = AsmResolverUtils.MakeConstant(defaultValue);

                managedMethod.ParameterDefinitions.Add(managedParam);
            }

            if (managedMethod.IsManagedMethodWithBody())
                FillMethodBodyWithStub(managedMethod);

            //Handle generic parameters.
            method.GenericContainer?.GenericParameters.ToList()
                .ForEach(p =>
                {
                    if (SharedState.GenericParamsByIndexNew.TryGetValue(p.Index, out var gp))
                    {
                        if (!managedMethod.GenericParameters.Contains(gp))
                            managedMethod.GenericParameters.Add(gp);

                        return;
                    }

                    gp = new(p.Name, (GenericParameterAttributes) p.flags);

                    if (!managedMethod.GenericParameters.Contains(gp))
                        managedMethod.GenericParameters.Add(gp);

                    p.ConstraintTypes!
                        .Select(c => new GenericParameterConstraint(importer.ImportTypeIfNeeded(AsmResolverUtils.GetTypeDefFromIl2CppType(importer, c).ToTypeDefOrRef())))
                        .ToList()
                        .ForEach(gp.Constraints.Add);
                });

            SharedState.UnmanagedToManagedMethodsNew[method] = managedMethod;
            SharedState.ManagedToUnmanagedMethodsNew[managedMethod] = method;
            ilTypeDefinition.Methods.Add(managedMethod);
        }
    }

    private static void CopyPropertiesInType(ReferenceImporter importer, Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition)
    {
        foreach (var property in cppTypeDefinition.Properties!)
        {
            var propertyTypeSig = importer.ImportTypeSignature(AsmResolverUtils.GetTypeDefFromIl2CppType(importer, property.RawPropertyType!).ToTypeSignature());
            var propertySignature = property.IsStatic
                ? PropertySignature.CreateStatic(propertyTypeSig)
                : PropertySignature.CreateInstance(propertyTypeSig);

            var managedProperty = new PropertyDefinition(property.Name, (PropertyAttributes) property.attrs, propertySignature);

            var managedGetter = property.Getter == null ? null : SharedState.UnmanagedToManagedMethodsNew[property.Getter];
            var managedSetter = property.Setter == null ? null : SharedState.UnmanagedToManagedMethodsNew[property.Setter];

            if (managedGetter != null)
                managedProperty.Semantics.Add(new(managedGetter, MethodSemanticsAttributes.Getter));

            if (managedSetter != null)
                managedProperty.Semantics.Add(new(managedSetter, MethodSemanticsAttributes.Setter));

            ilTypeDefinition.Properties.Add(managedProperty);
        }
    }

    private static void CopyEventsInType(ReferenceImporter importer, Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition)
    {
        foreach (var eventDef in cppTypeDefinition.Events!)
        {
            var eventType = importer.ImportTypeIfNeeded(AsmResolverUtils.GetTypeDefFromIl2CppType(importer, eventDef.RawType!).ToTypeDefOrRef());

            var managedEvent = new EventDefinition(eventDef.Name, (EventAttributes) eventDef.EventAttributes, eventType);

            var managedAdder = eventDef.Adder == null ? null : SharedState.UnmanagedToManagedMethodsNew[eventDef.Adder];
            var managedRemover = eventDef.Remover == null ? null : SharedState.UnmanagedToManagedMethodsNew[eventDef.Remover];
            var managedInvoker = eventDef.Invoker == null ? null : SharedState.UnmanagedToManagedMethodsNew[eventDef.Invoker];

            if (managedAdder != null)
                managedEvent.Semantics.Add(new(managedAdder, MethodSemanticsAttributes.AddOn));

            if (managedRemover != null)
                managedEvent.Semantics.Add(new(managedRemover, MethodSemanticsAttributes.RemoveOn));

            if (managedInvoker != null)
                managedEvent.Semantics.Add(new(managedInvoker, MethodSemanticsAttributes.Fire));

            ilTypeDefinition.Events.Add(managedEvent);
        }
    }

    private static void FillMethodBodyWithStub(MethodDefinition methodDefinition)
    {
        methodDefinition.CilMethodBody = new(methodDefinition);

        var methodInstructions = methodDefinition.CilMethodBody.Instructions;
        if (methodDefinition.Signature!.ReturnType.FullName == "System.Void")
        {
            methodInstructions.Add(CilOpCodes.Ret);
        }
        else if (methodDefinition.Signature!.ReturnType.IsValueType)
        {
            var variable = new CilLocalVariable(methodDefinition.Signature!.ReturnType);
            methodDefinition.CilMethodBody.LocalVariables.Add(variable);
            methodInstructions.Add(CilOpCodes.Ldloca_S, variable);
            methodInstructions.Add(CilOpCodes.Initobj, methodDefinition.Signature.ReturnType.ToTypeDefOrRef());
            methodInstructions.Add(CilOpCodes.Ldloc_0);
            methodInstructions.Add(CilOpCodes.Ret);
        }
        else
        {
            methodInstructions.Add(CilOpCodes.Ldnull);
            methodInstructions.Add(CilOpCodes.Ret);
        }
    }
}