using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class GenericInstanceTypeAnalysisContext : ReferencedTypeAnalysisContext
{
    public TypeAnalysisContext GenericType { get; }

    public List<TypeAnalysisContext> GenericArguments { get; } = [];

    public override TypeAttributes DefaultAttributes => GenericType.DefaultAttributes;

    public override TypeAttributes? OverrideAttributes { get => GenericType.OverrideAttributes; set => GenericType.OverrideAttributes = value; }

    public override string DefaultName => $"{GenericType.Name}<{string.Join(", ", GenericArguments.Select(a => a.Name))}>";

    public override string DefaultNamespace => GenericType.Namespace;

    public override TypeAnalysisContext? DefaultBaseType { get; }

    public sealed override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST;

    public sealed override bool IsGenericInstance => true;

    public sealed override bool IsValueType => GenericType.IsValueType; //We don't set a definition so the default implementation cannot determine if we're a value type or not. 

    private GenericInstanceTypeAnalysisContext(Il2CppType rawType, AssemblyAnalysisContext referencedFrom) : base(referencedFrom)
    {
        // Cache this instance before resolving anything else, which might contain a reference to this instance.
        // https://github.com/SamboyCoding/Cpp2IL/issues/469
        referencedFrom.GenericInstanceTypesByIl2CppType.TryAdd(rawType, this);

        //Generic type has to be a type definition
        var gClass = rawType.GetGenericClass();
        GenericType = AppContext.ResolveContextForType(gClass.TypeDefinition) ?? throw new($"Could not resolve type {gClass.TypeDefinition.FullName} for generic instance base type");

        GenericArguments.AddRange(gClass.Context.ClassInst.Types.Select(referencedFrom.ResolveIl2CppType)!);

        SetDeclaringType();
    }

    public GenericInstanceTypeAnalysisContext(TypeAnalysisContext genericType, IEnumerable<TypeAnalysisContext> genericArguments, AssemblyAnalysisContext referencedFrom) : base(referencedFrom)
    {
        GenericType = genericType;
        GenericArguments.AddRange(genericArguments);
        DefaultBaseType = genericType.BaseType;

        SetDeclaringType();
    }

    /// <summary>
    /// Get or create a <see cref="GenericInstanceTypeAnalysisContext"/> from an <see cref="Il2CppType"/>.
    /// </summary>
    /// <param name="rawType">The underlying <see cref="Il2CppType"/>.</param>
    /// <param name="referencedFrom">The assembly that is referencing this generic instance.</param>
    /// <returns>The context for the <paramref name="rawType"/>.</returns>
    public static GenericInstanceTypeAnalysisContext GetOrCreate(Il2CppType rawType, AssemblyAnalysisContext referencedFrom)
    {
        if (rawType.Type != Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
            throw new ArgumentException($"Cannot create {nameof(GenericInstanceTypeAnalysisContext)} from type {rawType.Type}. Expected {Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST}.");

        if (!referencedFrom.GenericInstanceTypesByIl2CppType.TryGetValue(rawType, out var result))
        {
            result = new GenericInstanceTypeAnalysisContext(rawType, referencedFrom);
            Debug.Assert(referencedFrom.GenericInstanceTypesByIl2CppType.ContainsKey(rawType), $"The {nameof(GenericInstanceTypeAnalysisContext)} constructor should add itself to the dictionary.");
        }

        return result;
    }

    public override string GetCSharpSourceString()
    {
        var sb = new StringBuilder();

        sb.Append(GenericType.GetCSharpSourceString());
        sb.Append('<');
        var first = true;
        foreach (var genericArgument in GenericArguments)
        {
            if (!first)
                sb.Append(", ");
            else
                first = false;

            sb.Append(genericArgument.GetCSharpSourceString());
        }

        sb.Append('>');

        return sb.ToString();
    }

    private void SetDeclaringType()
    {
        var declaringType = GenericType.DeclaringType;
        if (declaringType is null)
            return;

        DeclaringType = declaringType.GenericParameters.Count == 0
            ? declaringType
            : declaringType.MakeGenericInstanceType(GenericArguments.Take(declaringType.GenericParameters.Count));
    }
}
