using System.Diagnostics.CodeAnalysis;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Collections;
using AsmResolver.DotNet.Signatures.Types;
using AsmResolver.PE.DotNet.Cil;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Utils.AsmResolver;

internal static class AsmResolverMethodFiller
{
    public static void FillManagedMethodBodies(AssemblyAnalysisContext asmContext)
    {
        foreach (var typeContext in asmContext.Types)
        {
            if (AsmResolverAssemblyPopulator.IsTypeContextModule(typeContext))
                continue;

#if !DEBUG
            try
#endif
            {
                foreach (var methodCtx in typeContext.Methods)
                {
                    var managedMethod = methodCtx.GetExtraData<MethodDefinition>("AsmResolverMethod") ?? throw new($"AsmResolver method not found in method analysis context for {typeContext.Definition?.FullName}.{methodCtx.Definition?.Name}");

                    if (managedMethod.IsManagedMethodWithBody())
                        FillMethodBodyWithStub(managedMethod);
                }
            }
#if !DEBUG
            catch (System.Exception e)
            {
                var managedType = typeContext.GetExtraData<TypeDefinition>("AsmResolverType") ?? throw new($"AsmResolver type not found in type analysis context for {typeContext.Definition?.FullName}");
                throw new($"Failed to process type {managedType.FullName} (module {managedType.Module?.Name}, declaring type {managedType.DeclaringType?.FullName}) in {asmContext.Definition.AssemblyName.Name}", e);
            }
#endif
        }
    }

    private static void FillMethodBodyWithStub(MethodDefinition methodDefinition)
    {
        methodDefinition.CilMethodBody = new(methodDefinition);
        var methodInstructions = methodDefinition.CilMethodBody.Instructions;

        if (methodDefinition.IsConstructor && !methodDefinition.IsStatic && !methodDefinition.DeclaringType!.IsValueType)
        {
            var baseConstructor = TryGetBaseConstructor(methodDefinition);
            if (baseConstructor is not null)
            {
                methodInstructions.Add(CilOpCodes.Ldarg_0);
                foreach (var baseParameter in baseConstructor.Parameters)
                {
                    var importedBaseParameterType = methodDefinition.DeclaringType.Module!.DefaultImporter.ImportTypeSignature(baseParameter.ParameterType);
                    methodInstructions.AddDefaultValueForType(importedBaseParameterType);
                }
                methodInstructions.Add(CilOpCodes.Call, methodDefinition.DeclaringType.Module!.DefaultImporter.ImportMethod(baseConstructor));
            }
        }

        foreach (var parameter in methodDefinition.Parameters)
        {
            //Although Roslyn-compiled code will only emit the out flag on ByReferenceTypeSignatures,
            //Some Unity libraries have it on a handful (less than 100) of parameters with incompatible type signatures.
            //One example on 2021.3.6 is int System.IO.CStreamReader.Read([In][Out] char[] dest, int index, int count)
            //All the instances I investigated were clearly not meant to be out parameters.
            //The [In][Out] attributes are not a decompilation issue and compile fine on .NET 7.
            if (parameter.IsOutParameter(out var parameterType))
            {
                if (parameterType.IsValueTypeOrGenericParameter())
                {
                    methodInstructions.Add(CilOpCodes.Ldarg, parameter);
                    methodInstructions.Add(CilOpCodes.Initobj, parameterType.ToTypeDefOrRef());
                }
                else
                {
                    methodInstructions.Add(CilOpCodes.Ldarg, parameter);
                    methodInstructions.Add(CilOpCodes.Ldnull);
                    methodInstructions.Add(CilOpCodes.Stind_Ref);
                }
            }
        }
        methodInstructions.AddDefaultValueForType(methodDefinition.Signature!.ReturnType);
        methodInstructions.Add(CilOpCodes.Ret);
        methodInstructions.OptimizeMacros();
    }

    /// <summary>
    /// Is this <see cref="Parameter"/> an out parameter?
    /// </summary>
    /// <param name="parameter"></param>
    /// <param name="parameterType">The base type of the <see cref="ByReferenceTypeSignature"/></param>
    /// <returns></returns>
    private static bool IsOutParameter(this Parameter parameter, [NotNullWhen(true)] out TypeSignature? parameterType)
    {
        if ((parameter.Definition?.IsOut ?? false) && parameter.ParameterType is ByReferenceTypeSignature byReferenceTypeSignature)
        {
            parameterType = byReferenceTypeSignature.BaseType;
            return true;
        }
        else
        {
            parameterType = default;
            return false;
        }
    }

    private static MethodDefinition? TryGetBaseConstructor(MethodDefinition methodDefinition)
    {
        var declaringType = methodDefinition.DeclaringType!;
        var baseType = declaringType.BaseType?.Resolve();
        if (baseType is null)
        {
            return null;
        }
        else if (declaringType.Module == baseType.Module)
        {
            return baseType.Methods.FirstOrDefault(m => m.IsConstructor && !m.IsStatic && !m.IsPrivate);
        }
        else
        {
            return baseType.Methods.FirstOrDefault(m => m.IsConstructor && !m.IsStatic && (m.IsFamily || m.IsPublic));
        }
    }
}
