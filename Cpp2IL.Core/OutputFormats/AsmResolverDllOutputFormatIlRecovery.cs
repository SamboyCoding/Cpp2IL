using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AsmResolver.DotNet;
using AsmResolver.PE.DotNet.Cil;
using AssetRipper.CIL;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.OutputFormats;

public class AsmResolverDllOutputFormatIlRecovery : AsmResolverDllOutputFormat
{
    public override string OutputFormatId => "dll_il_recovery";

    public override string OutputFormatName => "DLL files with IL Recovery";

    private MethodDefinition? _exceptionConstructor;

    protected override void FillMethodBody(MethodDefinition methodDefinition, MethodAnalysisContext methodContext)
    {
        if (_exceptionConstructor == null)
            FindExceptionConstructor(methodDefinition);

        var module = methodDefinition.Module!;
        var moduleName = module.Name!.ToString();
        var shouldSkip = moduleName.StartsWith("UnityEngine.") || moduleName.StartsWith("Unity.") ||
                         moduleName.StartsWith("System.") || moduleName == "System" ||
                         moduleName.StartsWith("mscorlib");
        var importer = new ReferenceImporter(module);

        if (!methodDefinition.IsManagedMethodWithBody())
            return;

        methodDefinition.CilMethodBody = new(methodDefinition);
        var instructions = methodDefinition.CilMethodBody.Instructions;

        if (shouldSkip)
        {
            instructions.Add(CilOpCodes.Ldnull);
            instructions.Add(CilOpCodes.Throw);
            return;
        }

        try
        {
            TotalMethodCount++;

            methodContext.Analyze();

            // throw new Exception(isil);
            // i have no idea why but when doing this for 1 class it's fine, but with entire game strings get all messed up
            /* instructions.Add(CilOpCodes.Ldstr, string.Join("\n ", methodContext.ConvertedIsil));
            instructions.Add(CilOpCodes.Newobj, importer.ImportMethod(_exceptionConstructor!));
            instructions.Add(CilOpCodes.Throw); */

            instructions.Add(CilOpCodes.Ldnull);
            instructions.Add(CilOpCodes.Throw);

            //WriteControlFlowGraph(methodContext, Path.Combine(Environment.CurrentDirectory, "Cpp2IL", "bin", "Debug", "net9.0", "cpp2il_out", "cfg"));

            SuccessfulMethodCount++;
        }
        catch (Exception e)
        {
            Logger.ErrorNewline($"Decompiling {methodContext.FullName} failed: {e}");

            // throw new Exception(error);
            instructions.Add(CilOpCodes.Ldstr, e.ToString());
            instructions.Add(CilOpCodes.Newobj, importer.ImportMethod(_exceptionConstructor!));
            instructions.Add(CilOpCodes.Throw);
        }

        methodContext.ReleaseAnalysisData();
    }

    private void FindExceptionConstructor(MethodDefinition method)
    {
        var module = method.Module!;
        var mscorlibReference = module.AssemblyReferences.First(a => a.Name == "mscorlib");
        var mscorlib = mscorlibReference.Resolve()!.Modules[0];

        var exception = mscorlib.TopLevelTypes.First(t => t.FullName == "System.Exception");
        _exceptionConstructor = exception.Methods.First(m =>
            m.Name == ".ctor" && m.Parameters is [{ ParameterType.FullName: "System.String" }]);
    }

    private static void WriteControlFlowGraph(MethodAnalysisContext method, string outputPath)
    {
        var graph = method.ControlFlowGraph;

        var sb = new StringBuilder();
        var edges = new List<(int, int)>();

        sb.AppendLine("digraph ControlFlowGraph {");
        sb.AppendLine("    \"label\"=\"Control flow graph\"");

        if (graph == null) // no instructions
            graph = new Graphs.ISILControlFlowGraph();

        foreach (var block in graph.Blocks)
        {
            if (block == graph.EntryBlock || block == graph.ExitBlock)
            {
                var isEntry = block == graph.EntryBlock;
                sb.AppendLine($"""
                               	{block.ID} [
                               		"color"="{(isEntry ? "green" : "red")}"
                               		"label"="{(isEntry ? "Entry" : "Exit")} ({block.ID})"
                               	]
                               """);
            }
            else
            {
                sb.AppendLine($"""
                               	{block.ID} [
                               		"shape"="box"
                               		"label"="{block.ToString().EscapeString().Replace("\\r", "")}"
                               	]
                               """);
            }

            edges.AddRange(block.Successors.Select(b => (block.ID, b.ID)));
        }

        foreach (var edge in edges)
            sb.AppendLine($"    {edge.Item1} -> {edge.Item2}");

        sb.AppendLine("}");

        var type = method.DeclaringType!;
        var assemblyName = MiscUtils.CleanPathElement(type.DeclaringAssembly.CleanAssemblyName);
        var typePath = Path.Combine(type.FullName.Split('.').Select(MiscUtils.CleanPathElement).ToArray());
        var directoryPath = Path.Combine(outputPath, assemblyName, typePath);

        var methodName = MiscUtils.CleanPathElement(method.Name + "_" + string.Join("_",
            method.Parameters.Select(p => MiscUtils.CleanPathElement(p.ParameterType.Name))));
        var path = Path.Combine(directoryPath, methodName) + ".dot";

        if (path.Length > 260)
        {
            path = path[..250];
            path += ".dot";
        }

        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, sb.ToString());
    }
}
