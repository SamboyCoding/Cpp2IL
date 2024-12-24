using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Cil;
using AssetRipper.CIL;
using Cpp2IL.Core.Model.Contexts;

namespace Cpp2IL.Core.OutputFormats;

public class AsmResolverDllOutputFormatIlRecovery : AsmResolverDllOutputFormat
{
    public override string OutputFormatId => "dll_il_recovery";

    public override string OutputFormatName => "DLL files with IL Recovery";

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
