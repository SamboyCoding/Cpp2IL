using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Cil;
using AssetRipper.CIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.OutputFormats;

public class AsmResolverDllOutputFormatThrowNull : AsmResolverDllOutputFormat
{
    public override string OutputFormatId => "dll_throw_null";

    public override string OutputFormatName => "DLL files with method bodies containing throw null";

    protected override void FillMethodBody(MethodDefinition methodDefinition, MethodAnalysisContext methodContext)
    {
        if (methodDefinition.IsManagedMethodWithBody())
        {
            methodDefinition.CilMethodBody = new(methodDefinition);
            var instructions = methodDefinition.CilMethodBody.Instructions;
            instructions.Add(CilOpCodes.Ldnull);
            instructions.Add(CilOpCodes.Throw);
        }
    }
}
