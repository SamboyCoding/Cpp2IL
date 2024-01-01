using System.Collections.Generic;
using System.Linq;
using System.Text;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures.Types;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class GenericInstanceTypeAnalysisContext : ReferencedTypeAnalysisContext
{
    protected override TypeAnalysisContext ElementType { get; }

    public override string DefaultName => $"{ElementType.Name}<{string.Join(", ", GenericArguments.Select(a => a.Name))}>";

    public override Il2CppTypeEnum Type => Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST;

    public GenericInstanceTypeAnalysisContext(Il2CppType rawType, AssemblyAnalysisContext referencedFrom) : base(referencedFrom)
    {
        //Element type has to be a type definition
        var gClass = rawType.GetGenericClass();
        ElementType = AppContext.ResolveContextForType(gClass.TypeDefinition) ?? throw new($"Could not resolve type {gClass.TypeDefinition.FullName} for generic instance base type");
        
        GenericArguments.AddRange(gClass.Context.ClassInst.Types.Select(referencedFrom.ResolveIl2CppType)!);
    }

    public GenericInstanceTypeAnalysisContext(TypeAnalysisContext elementType, IEnumerable<TypeAnalysisContext> genericArguments, AssemblyAnalysisContext referencedFrom) : base(referencedFrom)
    {
        ElementType = elementType;
        GenericArguments.AddRange(genericArguments);
        OverrideBaseType = elementType.BaseType;
    }

    public override TypeSignature ToTypeSignature(ModuleDefinition parentModule)
    {
        var elementType = ElementType.ToTypeSignature(parentModule).ToTypeDefOrRef();
        var genericArguments = GenericArguments.Select(a => a.ToTypeSignature(parentModule)).ToArray();

        return new GenericInstanceTypeSignature(elementType, IsValueType, genericArguments);
    }

    public override string GetCSharpSourceString()
    {
        var sb = new StringBuilder();

        sb.Append(ElementType.GetCSharpSourceString());
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
