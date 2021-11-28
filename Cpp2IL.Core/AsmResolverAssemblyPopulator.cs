using System.Linq;
using AsmResolver.DotNet;
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

            //Type generic params.
            PopulateGenericParamsForType(il2CppTypeDef, typeDefinition);

            //Set base type
            if (il2CppTypeDef.RawBaseType is { } parent)
                typeDefinition.BaseType = AsmResolverUtils.GetTypeDefFromIl2CppType(parent).ToTypeDefOrRef();

            //Set interfaces
            foreach (var interfaceType in il2CppTypeDef.RawInterfaces)
                typeDefinition.Interfaces.Add(new(AsmResolverUtils.GetTypeDefFromIl2CppType(interfaceType).ToTypeDefOrRef()));
        }
    }

    private static void PopulateGenericParamsForType(Il2CppTypeDefinition cppTypeDefinition, TypeDefinition ilTypeDefinition)
    {
        if (cppTypeDefinition.GenericContainer == null)
            return;

        foreach (var param in cppTypeDefinition.GenericContainer.GenericParameters)
        {
            if (!SharedState.GenericParamsByIndexNew.TryGetValue(param.Index, out var p))
            {
                p = new GenericParameter(param.Name, (GenericParameterAttributes) param.flags);
                SharedState.GenericParamsByIndexNew[param.Index] = p;

                ilTypeDefinition.GenericParameters.Add(p);

                param.ConstraintTypes!
                    .Select(c => new GenericParameterConstraint(AsmResolverUtils.GetTypeDefFromIl2CppType(c).ToTypeDefOrRef()))
                    .ToList()
                    .ForEach(p.Constraints.Add);
            }
            else if (!ilTypeDefinition.GenericParameters.Contains(p))
                ilTypeDefinition.GenericParameters.Add(p);
        }
    }
}