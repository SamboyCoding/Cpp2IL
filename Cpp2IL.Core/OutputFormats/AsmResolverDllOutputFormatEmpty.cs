using AsmResolver.DotNet;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils.AsmResolver;

namespace Cpp2IL.Core.OutputFormats;

public class AsmResolverDllOutputFormatEmpty : AsmResolverDllOutputFormat
{
    public override string OutputFormatId => "dll_empty";

    public override string OutputFormatName => "DLL files with empty method bodies";

    protected override void FillMethodBody(MethodDefinition methodDefinition, MethodAnalysisContext methodContext)
    {
        if (methodDefinition.IsManagedMethodWithBody())
            methodDefinition.CilMethodBody = new(methodDefinition);
    }
}
