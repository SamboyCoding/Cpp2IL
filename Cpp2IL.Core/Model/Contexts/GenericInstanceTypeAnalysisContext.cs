using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class GenericInstanceTypeAnalysisContext : ReferencedTypeAnalysisContext
{
    public TypeAnalysisContext GenericType { get; }

    public List<TypeAnalysisContext> GenericArguments { get; }

    public sealed override TypeAttributes DefaultAttributes => GenericType.DefaultAttributes;

    public sealed override TypeAttributes? OverrideAttributes { get => GenericType.OverrideAttributes; set => GenericType.OverrideAttributes = value; }

    public sealed override string DefaultName => $"{GenericType.DefaultName}<{string.Join(", ", GenericArguments.Select(a => a.DefaultFullName))}>";

    public sealed override string? OverrideName
    {
        get => $"{GenericType.Name}<{string.Join(", ", GenericArguments.Select(a => a.FullName))}>";
        set => throw new NotSupportedException();
    }

    public sealed override string DefaultNamespace => GenericType.DefaultNamespace;

    public sealed override string? OverrideNamespace
    {
        get => GenericType.OverrideNamespace;
        set => GenericType.OverrideNamespace = value;
    }

    public sealed override TypeAnalysisContext? DefaultBaseType
    {
        get
        {
            field ??= GenericType.DefaultBaseType is null ? null : GenericInstantiation.Instantiate(GenericType.DefaultBaseType, GenericArguments, []);
            return field;
        }
    }

    public sealed override TypeAnalysisContext? OverrideBaseType
    {
        get
        {
            if (base.OverrideBaseType is null && GenericType.OverrideBaseType is not null)
            {
                base.OverrideBaseType = GenericInstantiation.Instantiate(GenericType.OverrideBaseType, GenericArguments, []);
            }
            return base.OverrideBaseType;
        }
        set => throw new NotSupportedException();
    }

    public sealed override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST;

    public sealed override bool IsGenericInstance => true;

    public sealed override bool IsValueType => GenericType.IsValueType; //We don't set a definition so the default implementation cannot determine if we're a value type or not. 

    // instances being constructed on the current thread, keyed by their cache identity (see GetOrCreate for why)
    [ThreadStatic] private static Dictionary<(ApplicationAnalysisContext, Il2CppType), GenericInstanceTypeAnalysisContext>? _underConstruction1;
    [ThreadStatic] private static Dictionary<(ApplicationAnalysisContext, TypeAnalysisContext, GenericArgumentList), GenericInstanceTypeAnalysisContext>? _underConstruction2;

    private GenericInstanceTypeAnalysisContext(Il2CppType rawType, ApplicationAnalysisContext context) : base(context.ResolveContextForAssembly(rawType.GetGenericClass().TypeDefinition.DeclaringAssembly!))
    {
        var underConstruction1 = _underConstruction1 ??= new();
        underConstruction1[(context, rawType)] = this;
        try
        {
            //Generic type has to be a type definition
            var gClass = rawType.GetGenericClass();
            GenericType = AppContext.ResolveContextForType(gClass.TypeDefinition) ?? throw new($"Could not resolve type {gClass.TypeDefinition.FullName} for generic instance base type");

            GenericArguments = [.. gClass.Context.ClassInst!.Types.Select(t => context.ResolveIl2CppType(t))];

            var underConstruction2 = _underConstruction2 ??= new();
            underConstruction2.Add((context, GenericType, GenericArguments), this);
            try
            {
                SetDeclaringType();
            }
            finally
            {
                underConstruction2.Remove((context, GenericType, GenericArguments));
            }
        }
        finally
        {
            underConstruction1.Remove((context, rawType));
        }
    }

    private GenericInstanceTypeAnalysisContext(TypeAnalysisContext genericType, List<TypeAnalysisContext> genericArguments) : base(genericType.DeclaringAssembly)
    {
        GenericType = genericType;
        GenericArguments = genericArguments;

        var underConstruction = _underConstruction2 ??= new();
        underConstruction.Add((genericType.AppContext, genericType, genericArguments), this);
        try
        {
            SetDeclaringType();
        }
        finally
        {
            underConstruction.Remove((genericType.AppContext, genericType, genericArguments));
        }
    }

    /// <summary>
    /// Get or create a <see cref="GenericInstanceTypeAnalysisContext"/> from an <see cref="Il2CppType"/>.
    /// </summary>
    /// <param name="rawType">The underlying <see cref="Il2CppType"/>.</param>
    /// <param name="referencedFrom">The assembly that is referencing this generic instance.</param>
    /// <returns>The context for the <paramref name="rawType"/>.</returns>
    public static GenericInstanceTypeAnalysisContext GetOrCreate(Il2CppType rawType, ApplicationAnalysisContext referencedFrom)
    {
        if (rawType.Type != Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST)
            throw new ArgumentException($"Cannot create {nameof(GenericInstanceTypeAnalysisContext)} from type {rawType.Type}. Expected {Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST}.");

        // A self-referencing generic re-enters here while we're still building it (#469). In this case, the constructing thread gets 
        // its own in-progress instance from a thread-local map instead (otherwise it recurses into Lazy.Value, which throws).
        if (_underConstruction1 != null && _underConstruction1.TryGetValue((referencedFrom, rawType), out var partial))
            return partial;

        return referencedFrom.GenericInstanceTypesByIl2CppType
            .GetOrAdd(rawType, key => new Lazy<GenericInstanceTypeAnalysisContext>(() => new GenericInstanceTypeAnalysisContext(key, referencedFrom)))
            .Value;
    }

    public static GenericInstanceTypeAnalysisContext GetOrCreate(TypeAnalysisContext genericType, IEnumerable<TypeAnalysisContext> genericArguments)
    {
        var genericArgumentsList = genericArguments.ToList();

        // A self-referencing generic re-enters here while we're still building it (#469). In this case, the constructing thread gets 
        // its own in-progress instance from a thread-local map instead (otherwise it recurses into Lazy.Value, which throws).
        if (_underConstruction2 != null && _underConstruction2.TryGetValue((genericType.AppContext, genericType, genericArgumentsList), out var partial))
            return partial;

        return genericType.AppContext.GenericInstanceTypesByConstruction
            .GetOrAdd((genericType, genericArgumentsList), key => new Lazy<GenericInstanceTypeAnalysisContext>(() => new GenericInstanceTypeAnalysisContext(key.Item1, key.Item2)))
            .Value;
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

    protected override List<TypeAnalysisContext> GetInterfaceContexts()
    {
        return GenericType.InterfaceContexts.Select(i => GenericInstantiation.Instantiate(i, GenericArguments, [])).ToList();
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
