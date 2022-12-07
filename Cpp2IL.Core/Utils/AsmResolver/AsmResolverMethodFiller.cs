using System;
using System.Linq;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.DotNet.Signatures.Types;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.Utils.AsmResolver;

internal static class AsmResolverMethodFiller
{
    public static void FillManagedMethodBodies(AssemblyAnalysisContext asmContext)
    {
        var managedAssembly = asmContext.GetExtraData<AssemblyDefinition>("AsmResolverAssembly") ?? throw new("AsmResolver assembly not found in assembly analysis context for " + asmContext);

        var injectedRefHelperMethod = MakeRefHelper(managedAssembly.ManifestModule!);

        foreach (var typeContext in asmContext.Types)
        {
            if (typeContext.Name == "<Module>")
                continue;

            var managedType = typeContext.GetExtraData<TypeDefinition>("AsmResolverType") ?? throw new($"AsmResolver type not found in type analysis context for {typeContext.Definition?.FullName}");

#if !DEBUG
            try
#endif
            {
                foreach (var methodCtx in typeContext.Methods)
                {
                    var managedMethod = methodCtx.GetExtraData<MethodDefinition>("AsmResolverMethod") ?? throw new($"AsmResolver method not found in method analysis context for {typeContext.Definition?.FullName}.{methodCtx.Definition?.Name}");

                    if (managedMethod.IsManagedMethodWithBody())
                        FillMethodBodyWithStub(managedMethod, injectedRefHelperMethod);
                }
            }
#if !DEBUG
            catch (Exception e)
            {
                throw new Exception($"Failed to process type {managedType.FullName} (module {managedType.Module?.Name}, declaring type {managedType.DeclaringType?.FullName}) in {asmContext.Definition.AssemblyName.Name}", e);
            }
#endif
        }
    }

    private static void AddDefaultValueForType(CilInstructionCollection instructions, TypeSignature type)
    {
        if (type is CorLibTypeSignature { ElementType: ElementType.Void })
        {
        }
        else if (type.IsValueType)
        {
            var variable = instructions.AddLocalVariable(type);
            instructions.Add(CilOpCodes.Ldloca, variable);
            instructions.Add(CilOpCodes.Initobj, type.ToTypeDefOrRef());
            instructions.Add(CilOpCodes.Ldloc, variable);
        }
        else
        {
            instructions.Add(CilOpCodes.Ldnull);
        }
    }

    private static void FillMethodBodyWithStub(MethodDefinition methodDefinition, MethodDefinition injectedRefHelperMethod)
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
                    var importedBaseParameterType = methodDefinition.DeclaringType.Module!.DefaultImporter.ImportTypeSignatureIfNeeded(baseParameter.ParameterType);
                    if (baseParameter.Definition is { IsOut: true })
                    {
                        var variable = methodInstructions.AddLocalVariable(importedBaseParameterType);
                        methodInstructions.Add(CilOpCodes.Ldloca, variable);
                    }
                    else if (baseParameter.Definition is { IsIn: true })
                    {
                        var variable = methodInstructions.AddLocalVariable(importedBaseParameterType);
                        if (importedBaseParameterType.IsValueType)
                        {
                            methodInstructions.Add(CilOpCodes.Ldloca, variable);
                            methodInstructions.Add(CilOpCodes.Initobj, importedBaseParameterType.ToTypeDefOrRef());
                        }
                        else
                        {
                            methodInstructions.Add(CilOpCodes.Ldnull);
                            methodInstructions.Add(CilOpCodes.Stloc, variable);
                        }
                        methodInstructions.Add(CilOpCodes.Ldloca, variable);
                    }
                    else if (importedBaseParameterType is ByReferenceTypeSignature byReferenceTypeSignature)
                    {
                        var referencedType = byReferenceTypeSignature.BaseType;
                        var genericRefHelperInstance = injectedRefHelperMethod.DeclaringType!.MakeGenericInstanceType(referencedType);
                        var memberReference = new MemberReference(genericRefHelperInstance.ToTypeDefOrRef(), injectedRefHelperMethod.Name, injectedRefHelperMethod.Signature);
                        methodInstructions.Add(CilOpCodes.Call, memberReference);
                    }
                    else
                    {
                        AddDefaultValueForType(methodInstructions, importedBaseParameterType);
                    }
                }
                methodInstructions.Add(CilOpCodes.Call, methodDefinition.DeclaringType.Module!.DefaultImporter.ImportMethod(baseConstructor));
            }
        }

        foreach (var parameter in methodDefinition.Parameters)
        {
            if (parameter.Definition?.IsOut ?? false)
            {
                if (parameter.ParameterType.IsValueType)
                {
                    methodInstructions.Add(CilOpCodes.Ldarg, parameter);
                    methodInstructions.Add(CilOpCodes.Initobj, parameter.ParameterType.ToTypeDefOrRef());
                }
                else
                {
                    methodInstructions.Add(CilOpCodes.Ldarg, parameter);
                    methodInstructions.Add(CilOpCodes.Ldnull);
                    methodInstructions.Add(CilOpCodes.Stind_Ref);
                }
            }
        }
        AddDefaultValueForType(methodInstructions, methodDefinition.Signature!.ReturnType);
        methodInstructions.Add(CilOpCodes.Ret);
        methodInstructions.OptimizeMacros();
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

    private static MethodDefinition MakeRefHelper(ModuleDefinition module)
    {
        var staticClass = new TypeDefinition("Cpp2ILInjected", "RefHelper", TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit);
        module.TopLevelTypes.Add(staticClass);
        staticClass.GenericParameters.Add(new GenericParameter("T"));

        var fieldType = new GenericParameterSignature(GenericParameterType.Type, 0);
        var fieldSignature = new FieldSignature(fieldType);
        var field = new FieldDefinition("backingField", FieldAttributes.Private | FieldAttributes.Static, fieldSignature);
        staticClass.Fields.Add(field);

        var staticConstructorSignature = MethodSignature.CreateStatic(module.CorLibTypeFactory.Void);
        var staticConstructor = new MethodDefinition("..ctor", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RuntimeSpecialName, staticConstructorSignature);
        staticConstructor.CilMethodBody = new CilMethodBody(staticConstructor);
        var staticConstructorInstructions = staticConstructor.CilMethodBody.Instructions;
        staticConstructorInstructions.Add(CilOpCodes.Ldsflda, field);
        staticConstructorInstructions.Add(CilOpCodes.Initobj, fieldType.ToTypeDefOrRef());
        staticConstructorInstructions.Add(CilOpCodes.Ret);
        
        var genericInstance = staticClass.MakeGenericInstanceType(fieldType);
        var memberReference = new MemberReference(genericInstance.ToTypeDefOrRef(), field.Name, field.Signature);

        var methodSignature = MethodSignature.CreateStatic(new ByReferenceTypeSignature(fieldType));
        var method = new MethodDefinition("GetSharedReference", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, methodSignature);
        staticClass.Methods.Add(method);
        method.CilMethodBody = new CilMethodBody(method);
        var methodInstructions = method.CilMethodBody.Instructions;
        methodInstructions.Add(CilOpCodes.Ldsflda, memberReference);
        methodInstructions.Add(CilOpCodes.Ret);

        return method;
    }
}
