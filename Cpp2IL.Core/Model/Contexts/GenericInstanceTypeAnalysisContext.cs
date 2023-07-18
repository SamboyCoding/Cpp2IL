using System.Linq;
using System.Text;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class GenericInstanceTypeAnalysisContext : ReferencedTypeAnalysisContext
{
    protected override TypeAnalysisContext ElementType { get; }

    public GenericInstanceTypeAnalysisContext(Il2CppType rawType, AssemblyAnalysisContext referencedFrom) : base(rawType, referencedFrom)
    {
        //Element type has to be a type definition
        var gClass = rawType.GetGenericClass();
        ElementType = AppContext.ResolveContextForType(gClass.TypeDefinition) ?? throw new($"Could not resolve type {gClass.TypeDefinition.FullName} for generic instance base type");
        
        GenericArguments.AddRange(gClass.Context.ClassInst.Types.Select(referencedFrom.ResolveIl2CppType)!);
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
