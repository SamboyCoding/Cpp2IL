using System;
using System.IO;
using System.Linq;
using System.Text;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using LibCpp2IL;
using LibCpp2IL.Wasm;

namespace Cpp2IL.Core.OutputFormats;

public class WasmNameSectionOutputFormat : Cpp2IlOutputFormat
{
    public override string OutputFormatId => "wasm_name_section";
    public override string OutputFormatName => "Webassembly name section";

    public override void DoOutput(ApplicationAnalysisContext context, string outputRoot)
    {
        // Outputs a binary file matching the standardized "name" WebAssembly custom section.
        // If the game's binary doesn't already have the same type of section (which is practically always),
        // all that's needed to combine the two is a simple `cat main.wasm section.dat > out.wasm`.
        // Using this modified binary in place of the original one provides extra information for debuggers.

        // The spec can be found here: https://webassembly.github.io/spec/core/appendix/custom.html#name-section
        // Some additional subsections have been proposed (already implemented in v8 & spidermonkey), but they don't seem to be helpful as of now.
        // The proposal overview can be found here: https://github.com/WebAssembly/extended-name-section/blob/main/proposals/extended-name-section/Overview.md

        if (context.Binary.GetType() != typeof(WasmFile))
            throw new Exception("This output format only works with WebAssembly files");

        if (!Directory.Exists(outputRoot))
            Directory.CreateDirectory(outputRoot);

        var outputFile = File.Create(Path.Combine(outputRoot, "namesection.dat"));

        var section =
            new MemoryStream(); // This stream is separate from outputFile because we need to know the size of the section before writing it
        section.WriteName("name"); // The section's name (`name`)

        var moduleNameSubsection = new MemoryStream();
        moduleNameSubsection.WriteName("Unity");
        section.WriteSizedData(moduleNameSubsection, 0x0); // Subsection id 0
        moduleNameSubsection.Dispose();

        var paramSuccessCount = 0;
        var paramFailCount = 0;

        var functionData = context.AllTypes.SelectMany(t => t.Methods)
            .Select(method => (method, definition: WasmUtils.TryGetWasmDefinition(method)))
            .Where(v => v.definition is not null)
            .Select(v =>
            {
                var trueParamCount = v.definition!.GetType((WasmFile)LibCpp2IlMain.Binary!).ParamTypes.Length;

                // Also see WasmUtils.BuildSignature
                var parameters = v.method.Parameters.Select(param => param.Name).ToList();

                if (!v.method.IsStatic)
                    parameters.Insert(0, "this");

                if (v.method.ReturnTypeContext is
                    { IsValueType: true, IsPrimitive: true, Definition: null or { Size: > 8 } })
                    parameters.Insert(0, "out");

                parameters.Add("methodInfo"); // Only for some methods...?

                if (trueParamCount != parameters.Count)
                {
                    // Logger.WarnNewline($"Failed param matching for {v.method.FullNameWithSignature}, calculated {parameters.Count} with there actually being {trueParamCount} ({string.Join(" ", parameters)})");
                    parameters.Clear();
                    paramFailCount++;
                }
                else
                {
                    paramSuccessCount++;
                }

                return (
                    Index: v.definition!.FunctionTableIndex,
                    Name: v.method.FullName,
                    Parameters: parameters
                );
            })
            .GroupBy(v => v.Index)
            .OrderBy(grouping => grouping.Key)
            .ToDictionary(
                grouping => grouping.Key,
                grouping => grouping.Select(v => (v.Name, v.Parameters)).ToList()
            );
        
        Logger.InfoNewline(
            $"Estimated parameter naming success rate: {(float)paramSuccessCount / (paramSuccessCount + paramFailCount) * 100:N2}%");

        var functionNameSubsection = new MemoryStream();
        functionNameSubsection.WriteLEB128Unsigned((ulong)functionData.Count); // vector length

        var localNameSubsection = new MemoryStream();
        localNameSubsection.WriteLEB128Unsigned((ulong)functionData.Count); // vector length

        foreach (var (idx, data) in functionData)
        {
            functionNameSubsection.WriteLEB128Unsigned((ulong)idx);
            localNameSubsection.WriteLEB128Unsigned((ulong)idx);

            functionNameSubsection.WriteName(data.Count == 1 ? data[0].Name : $"multiple_{data.Count}_{data[0].Name}");

            localNameSubsection.WriteLEB128Unsigned((ulong)data[0].Parameters.Count); // vector length
            for (var i = 0; i < data[0].Parameters.Count; i++)
            {
                localNameSubsection.WriteLEB128Unsigned((ulong)i);
                // Possible to include type here, but may make names excessively long
                localNameSubsection.WriteName(data.Count == 1
                    ? data[0].Parameters[i]
                    : $"multiple_{data.Count}_{data[0].Parameters[i]}");
            }
        }

        section.WriteSizedData(functionNameSubsection, 0x1); // Subsection id 1
        section.WriteSizedData(localNameSubsection, 0x2); // Subsection id 2

        outputFile.WriteSizedData(section, 0x0); // Section id 0 - custom
    }
}

public static class Extensions
{
    public static void WriteName(this Stream memoryStream, string name)
    {
        var bytes = Encoding.Default.GetBytes(name);
        memoryStream.WriteLEB128Unsigned((ulong)bytes.Length);
        memoryStream.Write(bytes, 0, bytes.Length);
    }

    public static void WriteSizedData(this Stream memoryStream, MemoryStream data, byte? prependByte = null)
    {
        if (prependByte.HasValue)
        {
            memoryStream.WriteByte(prependByte.Value);
        }

        memoryStream.WriteLEB128Unsigned((ulong)data.Length);
        data.WriteTo(memoryStream);
    }
}
