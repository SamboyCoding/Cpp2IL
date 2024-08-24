using System;
using System.IO;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL.Wasm;

namespace Cpp2IL.Core.OutputFormats;

public class WasmMappingOutputFormat : Cpp2IlOutputFormat
{
    public override string OutputFormatId => "wasmmappings";
    public override string OutputFormatName => "WebAssembly Method Mappings";

    public override void DoOutput(ApplicationAnalysisContext context, string outputRoot)
    {
        if (context.Binary is not WasmFile wasmFile)
            throw new("This output format only works with WebAssembly files");

        Logger.InfoNewline("Generating WebAssembly method mappings...This may take up to a minute...", "WasmMappingOutputFormat");
        var output = new StringBuilder();

        foreach (var assemblyAnalysisContext in context.Assemblies)
        {
            output.Append("// ").Append(assemblyAnalysisContext.Definition.AssemblyName.Name).Append(".dll").AppendLine().AppendLine();

            foreach (var typeAnalysisContext in assemblyAnalysisContext.Types)
            foreach (var methodAnalysisContext in typeAnalysisContext.Methods)
            {
                if (methodAnalysisContext is InjectedMethodAnalysisContext || methodAnalysisContext.Definition == null)
                    continue;

                output.Append(methodAnalysisContext.Definition.ReturnType)
                    .Append(' ')
                    .Append(methodAnalysisContext.DeclaringType!.FullName)
                    .Append("::")
                    .Append(methodAnalysisContext.Definition.Name)
                    .Append('(')
                    .Append(string.Join(", ", methodAnalysisContext.Definition.Parameters!.Select(p => $"{p.Type} {p.ParameterName}")))
                    .Append(") -> ");

                try
                {
                    var wasmDef = WasmUtils.GetWasmDefinition(methodAnalysisContext.Definition);
                    var ghidraName = WasmUtils.GetGhidraFunctionName(wasmDef);

                    output.AppendLine(ghidraName);
                }
                catch (Exception)
                {
                    output.AppendLine("<not resolved>");
                }
            }

            output.AppendLine().AppendLine();
        }

        var outPath = Path.Combine(outputRoot, "wasm_mappings.txt");
        File.WriteAllText(outPath, output.ToString());

        Logger.InfoNewline("Wasm mappings written to: " + outPath, "WasmMappingOutputFormat");
    }
}
