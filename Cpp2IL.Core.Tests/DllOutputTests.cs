using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AsmResolver;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Builder;
using AsmResolver.DotNet.Signatures;
using Cpp2IL.Core.OutputFormats;

namespace Cpp2IL.Core.Tests;

public class DllOutputTests
{
    [Test]
    public void AllAssembliesBuild()
    {
        var appContext = TestGameLoader.LoadSimple2019Game();

        var assemblies = new AsmResolverDllOutputFormatDefault().BuildAssemblies(appContext);

        using (Assert.EnterMultipleScope())
        {
            foreach (var assembly in assemblies)
            {
                Assert.DoesNotThrow(() =>
                {
                    using MemoryStream stream = new();
                    assembly.WriteManifest(stream, new ManagedPEImageBuilder(ThrowErrorListener.Instance));
                });
            }
        }
    }

    [Test]
    public void MscorlibIsItsOwnCorLib()
    {
        var appContext = TestGameLoader.LoadSimple2019Game();

        var assemblies = new AsmResolverDllOutputFormatEmpty().BuildAssemblies(appContext);

        var mscorlib = assemblies.First(a => a.Name == "mscorlib").ManifestModule!;

        Assert.That(SignatureComparer.Default.Equals(mscorlib.CorLibTypeFactory.CorLibScope, mscorlib));
    }

    [Test]
    public void MscorlibHasNoAssemblyReferences()
    {
        var appContext = TestGameLoader.LoadSimple2019Game();

        var assemblies = new AsmResolverDllOutputFormatEmpty().BuildAssemblies(appContext);

        var mscorlib = assemblies.First(a => a.Name == "mscorlib").ManifestModule!;

        Assert.That(mscorlib.AssemblyReferences, Is.Empty);
    }

    [Test]
    public void MscorlibHasNoTypeReferences()
    {
        var appContext = TestGameLoader.LoadSimple2019Game();

        var assemblies = new AsmResolverDllOutputFormatEmpty().BuildAssemblies(appContext);

        var mscorlib = assemblies.First(a => a.Name == "mscorlib").ManifestModule!;

        SearchForTypeReference(mscorlib);
    }

    private static void SearchForTypeReference(ModuleDefinition module)
    {
        if (ContainsTypeReference(module.CustomAttributes))
        {
            Assert.Fail($"Module {module} contains a type reference in its custom attributes");
        }
        if (ContainsTypeReference(module.Assembly?.CustomAttributes))
        {
            Assert.Fail($"Module {module} contains a type reference in its assembly custom attributes");
        }
        foreach (var typeDefinition in module.GetAllTypes())
        {
            // Type
            {
                if (ContainsTypeReference(typeDefinition.BaseType))
                {
                    Assert.Fail($"Type {typeDefinition} contains a type reference in its base type");
                }
                if (ContainsTypeReference(typeDefinition.Interfaces.Select(i => i.Interface)))
                {
                    Assert.Fail($"Type {typeDefinition} contains a type reference in its interfaces");
                }
                if (ContainsTypeReference(typeDefinition.GenericParameters.SelectMany(g => g.Constraints).Select(c => c.Constraint)))
                {
                    Assert.Fail($"Type {typeDefinition} contains a type reference in its generic parameter constraints");
                }
                foreach (var methodOverride in typeDefinition.MethodImplementations)
                {
                    if (methodOverride.Body is not MethodDefinition)
                    {
                        Assert.Fail($"Method override {methodOverride} in type {typeDefinition} does not have a method body");
                    }
                    if (ContainsTypeReference(methodOverride.Declaration?.Signature?.ReturnType))
                    {
                        Assert.Fail($"Method override {methodOverride} in type {typeDefinition} contains a type reference in its return type");
                    }
                    if (ContainsTypeReference(methodOverride.Declaration?.Signature?.ParameterTypes))
                    {
                        Assert.Fail($"Method override {methodOverride} in type {typeDefinition} contains a type reference in its parameter types");
                    }
                    if (methodOverride.Declaration is MethodSpecification methodSpecification && ContainsTypeReference(methodSpecification.Method?.Signature))
                    {
                        Assert.Fail($"Method override {methodOverride} in type {typeDefinition} contains a type reference in its method specification");
                    }
                }
                if (ContainsTypeReference(typeDefinition.CustomAttributes))
                {
                    Assert.Fail($"Type {typeDefinition} contains a type reference in its custom attributes");
                }
            }
            foreach (var field in typeDefinition.Fields)
            {
                if (ContainsTypeReference(field.Signature?.FieldType))
                {
                    Assert.Fail($"Field {field} in type {typeDefinition} contains a type reference in its field type");
                }
                if (ContainsTypeReference(field.CustomAttributes))
                {
                    Assert.Fail($"Field {field} in type {typeDefinition} contains a type reference in its custom attributes");
                }
            }
            foreach (var property in typeDefinition.Properties)
            {
                if (ContainsTypeReference(property.Signature?.ReturnType))
                {
                    Assert.Fail($"Property {property} in type {typeDefinition} contains a type reference in its return type");
                }
                if (ContainsTypeReference(property.Signature?.ParameterTypes))
                {
                    Assert.Fail($"Property {property} in type {typeDefinition} contains a type reference in its parameter types");
                }
                if (ContainsTypeReference(property.CustomAttributes))
                {
                    Assert.Fail($"Property {property} in type {typeDefinition} contains a type reference in its custom attributes");
                }
            }
            foreach (var @event in typeDefinition.Events)
            {
                if (ContainsTypeReference(@event.EventType))
                {
                    Assert.Fail($"Event {@event} in type {typeDefinition} contains a type reference in its event type");
                }
                if (ContainsTypeReference(@event.CustomAttributes))
                {
                    Assert.Fail($"Event {@event} in type {typeDefinition} contains a type reference in its custom attributes");
                }

            }
            foreach (var method in typeDefinition.Methods)
            {
                if (ContainsTypeReference(method.Signature?.ReturnType))
                {
                    Assert.Fail($"Method {method} in type {typeDefinition} contains a type reference in its return type");
                }
                if (ContainsTypeReference(method.Signature?.ParameterTypes))
                {
                    Assert.Fail($"Method {method} in type {typeDefinition} contains a type reference in its parameter types");
                }
                if (ContainsTypeReference(method.GenericParameters.SelectMany(g => g.Constraints).Select(c => c.Constraint)))
                {
                    Assert.Fail($"Method {method} in type {typeDefinition} contains a type reference in its generic parameter constraints");
                }
                if (ContainsTypeReference(method.CilMethodBody?.LocalVariables.Select(v => v.VariableType)))
                {
                    Assert.Fail($"Method {method} in type {typeDefinition} contains a type reference in its local variables");
                }
                if (ContainsTypeReference(method.CilMethodBody?.Instructions.Select(i => i.Operand as ITypeDefOrRef)))
                {
                    Assert.Fail($"Method {method} in type {typeDefinition} contains a type reference in its instructions");
                }
                if (ContainsTypeReference(method.CilMethodBody?.ExceptionHandlers.Select(h => h.ExceptionType)))
                {
                    Assert.Fail($"Method {method} in type {typeDefinition} contains a type reference in its exception handlers");
                }
                if (ContainsTypeReference(method.CustomAttributes))
                {
                    Assert.Fail($"Method {method} in type {typeDefinition} contains a type reference in its custom attributes");
                }
                if (ContainsTypeReference(method.ParameterDefinitions.SelectMany(p => p.CustomAttributes)))
                {
                    Assert.Fail($"Method {method} in type {typeDefinition} contains a type reference in its parameter custom attributes");
                }
            }
        }
    }

    private static bool ContainsTypeReference(IEnumerable<CustomAttribute>? customAttributes)
    {
        return customAttributes is not null && customAttributes.Any(ContainsTypeReference);
    }

    private static bool ContainsTypeReference(MethodSignature? methodSignature)
    {
        return ContainsTypeReference(methodSignature?.ReturnType)
            || ContainsTypeReference(methodSignature?.ParameterTypes);
    }

    private static bool ContainsTypeReference(CustomAttribute customAttribute)
    {
        return ContainsTypeReference(customAttribute.Constructor?.Signature) || ContainsTypeReference(customAttribute.Signature);
    }

    private static bool ContainsTypeReference(CustomAttributeSignature? signature)
    {
        if (signature is null)
            return false;

        return ContainsTypeReference(signature.FixedArguments.Select(a => a.ArgumentType))
            || ContainsTypeReference(signature.NamedArguments.Select(a => a.ArgumentType))
            || ContainsTypeReference(signature.NamedArguments.Select(a => a.Argument.ArgumentType));
    }

    private static bool ContainsTypeReference(TypeSignature? type)
    {
        return type switch
        {
            CorLibTypeSignature corLibTypeSignature => corLibTypeSignature.Scope is not ModuleDefinition,
            TypeDefOrRefSignature typeDefOrRefSignature => typeDefOrRefSignature.Type is not TypeDefinition,
            CustomModifierTypeSignature customModifierTypeSignature => ContainsTypeReference(customModifierTypeSignature.BaseType) || ContainsTypeReference(customModifierTypeSignature.ModifierType),
            TypeSpecificationSignature typeSpecificationSignature => ContainsTypeReference(typeSpecificationSignature.BaseType),
            GenericInstanceTypeSignature genericInstanceTypeSignature => ContainsTypeReference(genericInstanceTypeSignature.GenericType) || ContainsTypeReference(genericInstanceTypeSignature.TypeArguments),
            FunctionPointerTypeSignature functionPointerTypeSignature => ContainsTypeReference(functionPointerTypeSignature.Signature),
            _ => false, // null, GenericParameterSignature, SentinelTypeSignature
        };
    }

    private static bool ContainsTypeReference(IEnumerable<TypeSignature?>? types)
    {
        return types is not null && types.Any(ContainsTypeReference);
    }

    private static bool ContainsTypeReference(ITypeDefOrRef? typeDefOrRef)
    {
        return typeDefOrRef switch
        {
            null => false,
            InvalidTypeDefOrRef => true,
            TypeDefinition => false,
            TypeReference => true,
            TypeSpecification typeSpecification => ContainsTypeReference(typeSpecification.Signature),
            _ => throw new NotSupportedException(),
        };
    }

    private static bool ContainsTypeReference(IEnumerable<ITypeDefOrRef?>? types)
    {
        return types is not null && types.Any(ContainsTypeReference);
    }
}
