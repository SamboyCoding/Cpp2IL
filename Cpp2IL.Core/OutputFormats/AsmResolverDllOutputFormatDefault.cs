using AsmResolver.DotNet;
using AssetRipper.CIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.OutputFormats;

public class AsmResolverDllOutputFormatDefault : AsmResolverDllOutputFormat
{
    public override string OutputFormatId => "dll_default";

    public override string OutputFormatName => "DLL files with default method bodies";

    protected override void FillMethodBody(MethodDefinition methodDefinition, MethodAnalysisContext methodContext)
    {
        methodDefinition.FillMethodBodyWithStub();
    }
}
