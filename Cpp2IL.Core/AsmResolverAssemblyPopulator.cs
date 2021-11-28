using System;
using System.Linq;
using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.DotNet.Signatures.Types;
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
            var managedType = SharedState.UnmanagedToManagedTypesNew[il2CppTypeDefinition];

            try
            {
                CopyIl2CppDataToManagedType(il2CppTypeDefinition, managedType);
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to process type {managedType.FullName} (module {managedType.Module?.Name}, declaring type {managedType.DeclaringType?.FullName}) in {imageDef.Name}", e);
            }
        }
    }

    private static void CopyIl2CppDataToManagedType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition)
    {
        var importer = ilTypeDefinition.Module!.Assembly!.GetImporter();

        CopyFieldsInType(importer, cppTypeDefinition, ilTypeDefinition);
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
}