using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AssetRipper.CIL;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Graphs;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Core.OutputFormats;

public class AsmResolverDllOutputFormatIlRecovery : AsmResolverDllOutputFormat
{
    public override string OutputFormatId => "dll_il_recovery";

    public override string OutputFormatName => "DLL files with IL Recovery";

    public override List<AssemblyDefinition> BuildAssemblies(ApplicationAnalysisContext context)
    {
        //We're going to need key function addresses, so grab them. This way the logging is more consistent
        Logger.InfoNewline("Finding key function addresses...");
        var start = DateTime.Now;
        _ = context.GetOrCreateKeyFunctionAddresses();
        Logger.InfoNewline($"Key function addresses found in {DateTime.Now.Subtract(start).TotalMilliseconds}ms");

        IlGenerator.InjectHelpersType(context);

        return base.BuildAssemblies(context);
    }

    protected override void FillMethodBody(MethodDefinition methodDefinition, MethodAnalysisContext methodContext)
    {
        var module = methodDefinition.DeclaringModule!;
        var moduleName = module.Name!.ToString();
        var shouldSkip = moduleName.StartsWith("UnityEngine.") || moduleName.StartsWith("Unity.") ||
                         moduleName.StartsWith("System.") || moduleName == "System" ||
                         moduleName.StartsWith("mscorlib");

        if (!methodDefinition.IsManagedMethodWithBody())
            return;

        methodDefinition.CilMethodBody = new();
        var instructions = methodDefinition.CilMethodBody.Instructions;

        if (shouldSkip)
        {
            FillMethodBodyWithStub(methodDefinition);
            return;
        }

        try
        {
            Interlocked.Increment(ref TotalMethodCount);

            methodContext.Analyze();

            if (methodContext.ConvertedIsil.Count == 0)
                FillMethodBodyWithStub(methodDefinition);
            else
                IlGenerator.GenerateIl(methodContext, methodDefinition);

            //WriteControlFlowGraph(methodContext, Path.Combine(Environment.CurrentDirectory, "Cpp2IL", "bin", "Debug", "net9.0", "cpp2il_out", "cfg"));

            Interlocked.Increment(ref SuccessfulMethodCount);
        }
        catch (Exception e)
        {
            // Known analysis limitations (DecompilerException) get a one-line warning; anything
            // else is an unexpected bug and keeps its (collapsed) stack trace.
            var detail = e is DecompilerException ? e.Message : e.ToCollapsedString();

            if (detail.Length > 1000) // unbounded ldstrs can overflow the 24 bit #US heap offset space
                detail = detail[..1000] + "…";

            if (e is DecompilerException)
                Logger.WarnNewline($"Skipping {methodContext.FullName}: {e.Message}");
            else
                Logger.ErrorNewline($"Decompiling {methodContext.FullName} failed: {detail}");
            
            methodDefinition.CilMethodBody = new();
            instructions = methodDefinition.CilMethodBody.Instructions;

            var factory = module.CorLibTypeFactory;
            var exceptionCtor = factory.CorLibScope
                .CreateTypeReference("System", "Exception")
                .CreateMemberReference(".ctor", MethodSignature.CreateInstance(factory.Void, [factory.String]));

            instructions.Add(CilOpCodes.Ldstr, detail);
            instructions.Add(CilOpCodes.Newobj, exceptionCtor);
            instructions.Add(CilOpCodes.Throw);
        }

        methodContext.ReleaseAnalysisData();
    }

    public static void WriteControlFlowGraph(MethodAnalysisContext method, string outputPath)
    {
        var graph = method.ControlFlowGraph;

        var sb = new StringBuilder();
        var edges = new List<(int, int)>();

        sb.AppendLine("digraph ControlFlowGraph {");
        sb.AppendLine("    \"label\"=\"Control flow graph\"");

        // no instructions
        graph ??= new ISILControlFlowGraph([]);

        var methodText = $@"{CsFileUtils.GetKeyWordsForMethod(method)} {method.FullNameWithSignature}
parameter locals: {string.Join(", ", method.ParameterLocals)}
parameter operands: {string.Join(", ", method.ParameterOperands)}";

        foreach (var block in graph.Blocks)
        {
            if (block == graph.EntryBlock || block == graph.ExitBlock)
            {
                var isEntry = block == graph.EntryBlock;
                sb.AppendLine($"""
                               	{block.ID} [
                               		"color"="{(isEntry ? "green" : "red")}"
                               		"label"="{(isEntry ? $"Entry ({block.ID})\n{methodText}" : $"Exit ({block.ID})")}"
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
