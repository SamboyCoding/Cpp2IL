using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AssetRipper.CIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Cpp2IL.Core.Utils.AsmResolver;
using Decompiler;
using Decompiler.ControlFlow;
using Decompiler.IL;
using Logger = Cpp2IL.Core.Logging.Logger;

namespace Cpp2IL.Core.OutputFormats;

public class IlOutputFormat : AsmResolverDllOutputFormat
{
    public override string OutputFormatId => "il";
    public override string OutputFormatName => ".NET IL";

    private string[] NamespacesToSkip = ["UnityEngine.", "Unity.", "Mono.", "System.", "TMPro.", "Newtonsoft."];

    private Decompiler.Decompiler _decompiler = new();

    private static int _totalCount;
    private static int _processedCount;
    private static int _successCount;

    private static int _maxMethodSize = 50_000;

    public override void OnOutputFormatSelected()
    {
        base.OnOutputFormatSelected();
        NoParallel = true; // parallel makes it fail often
    }

    protected override void BeforeStart(ApplicationAnalysisContext context)
    {
        _totalCount = 0;
        _processedCount = 0;
        _successCount = 0;

        foreach (var assembly in context.Assemblies)
        {
            foreach (var type in assembly.Types)
            {
                if (NamespacesToSkip.Any(n => type.FullName.StartsWith(n)))
                    continue;

                _totalCount += type.Methods.Count;
            }
        }
    }

    protected override void FillMethodBody(MethodDefinition methodDefinition, MethodAnalysisContext methodContext)
    {
        if (NamespacesToSkip.Any(n => methodContext.DeclaringType!.FullName.StartsWith(n)))
            return;

        if (!methodDefinition.IsManagedMethodWithBody()) return;
        methodDefinition.CilMethodBody = new CilMethodBody(methodDefinition);

        _processedCount++;

        var progress = (float)_processedCount / _totalCount;
        var status = $"Decompiling {methodContext.FullName}";
        PrintProgressBar(progress, status);

        try
        {
            if (_maxMethodSize != -1 && methodContext.RawBytes.Length > _maxMethodSize)
                throw new LimitReachedException($"Too big method body in {methodContext.DeclaringType!.Name}.{methodContext.Name}! ({methodContext.RawBytes.Length} bytes)");

            var il = methodContext.AppContext.InstructionSet.GetDecompilerIlFromMethod(methodContext, out var ilParams);

            // Resolve calls now that we have .NET method definitions
            foreach (var instruction in il)
            {
                switch (instruction.OpCode)
                {
                    case OpCode.Call:
                    case OpCode.TailCall:
                        instruction.Operands[1] = ((MethodAnalysisContext)instruction.Operands[1]).ToMethodDescriptor(methodDefinition.Module!).Resolve()!;
                        break;
                    case OpCode.CallVoid:
                    case OpCode.TailCallVoid:
                        instruction.Operands[0] = ((MethodAnalysisContext)instruction.Operands[0]).ToMethodDescriptor(methodDefinition.Module!).Resolve()!;
                        break;
                }
            }

            var method = new Method(methodDefinition, il, ilParams);
            _decompiler.Decompile(method);

            var outputPath = Path.Combine(OutputPath, "CFG-Output");
            WriteControlFlowGraph(method.ControlFlowGraph, methodContext, methodDefinition, method, outputPath);

            _successCount++;
        }
        catch (LimitReachedException e)
        {
            Logger.WarnNewline(methodContext.FullName + ": " + e.Message, "IlOutputFormat");
        }
    }

    protected override void OnComplete()
    {
        Logger.InfoNewline($"{(Math.Round(((double)_successCount / _totalCount) * 100) / 100) * 100}% successfully decompiled ({_successCount} / {_totalCount})", "IlOutputFormat");
    }

    private static void PrintProgressBar(float progress, string status, int barWidth = 10)
    {
        // Clear current line
        var currentLineCursor = Console.CursorTop;
        Console.SetCursorPosition(0, Console.CursorTop);
        Console.Write(new string(' ', Console.BufferWidth));
        Console.SetCursorPosition(0, currentLineCursor);

        // Print progress
        var filled = (int)(progress * barWidth);
        var progressBar = "[" + new string('#', filled) + new string('_', barWidth - filled) + $"] {progress:P0} : {status}";

        Console.Write(progressBar + " ");
    }

    private static void WriteControlFlowGraph(ControlFlowGraph graph, MethodAnalysisContext method, MethodDefinition definition, Method decompilerMethod, string outputPath)
    {
        var sb = new StringBuilder();
        var edges = new List<(int, int)>();

        sb.AppendLine("digraph ControlFlowGraph {");
        sb.AppendLine("    \"label\"=\"Control flow graph\"");

        foreach (var block in graph.Blocks)
        {
            if (block == graph.EntryBlock || block == graph.ExitBlock)
            {
                var isEntry = block == graph.EntryBlock;
                sb.AppendLine($"""
                               	{block.Id} [
                               		"color"="{(isEntry ? "green" : "red")}"
                               		"label"="{(isEntry ? $@"Entry\n{definition}\nParams: {string.Join(", ", decompilerMethod.ParameterLocals)}\n" : "Exit")} ({block.Id})"
                               	]
                               """);
            }
            else
            {
                sb.AppendLine($"""
                               	{block.Id} [
                               		"shape"="box"
                               		"label"="{block.ToString().Replace("\"", "\\\"").Replace("\n", "\\n")}"
                               	]
                               """);
            }

            edges.AddRange(block.Successors.Select(b => (block.Id, b.Id)));
        }

        foreach (var edge in edges)
            sb.AppendLine($"    {edge.Item1} -> {edge.Item2}");

        sb.AppendLine("}");

        var assemblyName = MiscUtils.CleanPathElement(method.DeclaringType!.DeclaringAssembly.CleanAssemblyName);
        var typePath = Path.Combine(method.DeclaringType!.FullName.Split('.').Select(MiscUtils.CleanPathElement).ToArray());
        var directoryPath = Path.Combine(outputPath, assemblyName, typePath);
        var methodName = MiscUtils.CleanPathElement(method.Name + "_" + string.Join("_",
            method.Parameters.Select(p => MiscUtils.CleanPathElement(p.ParameterTypeContext.Name))));
        var path = Path.Combine(directoryPath, methodName) + ".dot";

        // Too long
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
