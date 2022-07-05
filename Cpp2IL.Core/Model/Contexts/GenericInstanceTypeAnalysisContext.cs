using System.Linq;
using Cpp2IL.Core.Utils;
using LibCpp2IL.BinaryStructures;

namespace Cpp2IL.Core.Model.Contexts;

public class GenericInstanceTypeAnalysisContext : ReferencedTypeAnalysisContext
{
    public GenericInstanceTypeAnalysisContext(Il2CppType rawType, AssemblyAnalysisContext referencedFrom) : base(rawType, referencedFrom)
    {
        //Element type has to be a type definition
        var gClass = rawType.GetGenericClass();
        ElementType = AppContext.ResolveContextForType(gClass.TypeDefinition) ?? throw new($"Could not resolve type {gClass.TypeDefinition.FullName} for generic instance base type");
        
        GenericArguments.AddRange(gClass.Context.ClassInst.Types.Select(referencedFrom.ResolveIl2CppType)!);
    }
}