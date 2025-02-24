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

    public List<TypeAnalysisContext> GenericArguments { get; } = [];

    public override TypeAttributes TypeAttributes => GenericType.TypeAttributes;

    public override string DefaultName => $"{GenericType.Name}<{string.Join(", ", GenericArguments.Select(a => a.Name))}>";

    public override string DefaultNs => GenericType.Namespace;

    public sealed override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST;

    public sealed override bool IsGenericInstance => true;

    public sealed override int GenericParameterCount => GenericArguments.Count;

    public sealed override bool IsValueType => GenericType.IsValueType; //We don't set a definition so the default implementation cannot determine if we're a value type or not. 

    public GenericInstanceTypeAnalysisContext(Il2CppType rawType, AssemblyAnalysisContext referencedFrom) : base(referencedFrom)
    {
        //Generic type has to be a type definition
        var gClass = rawType.GetGenericClass();
        GenericType = AppContext.ResolveContextForType(gClass.TypeDefinition) ?? throw new($"Could not resolve type {gClass.TypeDefinition.FullName} for generic instance base type");

        GenericArguments.AddRange(gClass.Context.ClassInst.Types.Select(referencedFrom.ResolveIl2CppType)!);
    }

    public GenericInstanceTypeAnalysisContext(TypeAnalysisContext genericType, IEnumerable<TypeAnalysisContext> genericArguments, AssemblyAnalysisContext referencedFrom) : base(referencedFrom)
    {
        GenericType = genericType;
        GenericArguments.AddRange(genericArguments);
        OverrideBaseType = genericType.BaseType;
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
}
