using AsmResolver.DotNet;
using AssetRipper.CIL;
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

                    managedMethod.FillMethodBodyWithStub();
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
}
