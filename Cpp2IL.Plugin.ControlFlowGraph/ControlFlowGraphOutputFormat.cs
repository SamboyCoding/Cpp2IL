using Cpp2IL.Core.Api;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;
using Cpp2IL.Core.Extensions;
using Rubjerg.Graphviz;
using System.Text;
using Cpp2IL.Core.Graphs;
using DotNetGraph.Core;
using DotNetGraph.Extensions;
using DotNetGraph.Attributes;
using DotNetGraph.Compilation;

namespace Cpp2IL.Plugin.ControlFlowGraph;

public class ControlFlowGraphOutputFormat : Cpp2IlOutputFormat
{
    public override string OutputFormatId => "cfg";
    public override string OutputFormatName => "CFG Dump";
    public override void DoOutput(ApplicationAnalysisContext context, string outputRoot)
    {
        outputRoot = Path.Combine(outputRoot, "CFGDump");

        var numAssemblies = context.Assemblies.Count;
        var i = 0;
        foreach (var assembly in context.Assemblies)
        {
            Logger.InfoNewline($"Processing assembly {i++} of {numAssemblies}: {assembly.Definition.AssemblyName.Name}", "ControlFlowGraphOutputFormat");

            var assemblyNameClean = assembly.CleanAssemblyName;

            MiscUtils.ExecuteParallel(assembly.Types, type  => 
            {
                if (type is InjectedTypeAnalysisContext)
                    return;

                if (type.Methods.Count == 0)
                    return;

                foreach (var method in type.Methods)
                {
                    if (method is InjectedMethodAnalysisContext)
                        continue;

                    try
                    {
                        method.Analyze();

                        if (method.ControlFlowGraph == null)
                        {
                            continue;
                        }

                        WriteMethodGraph(outputRoot, method, GenerateGraph(method.ControlFlowGraph, method), assemblyNameClean).Wait();

                        method.ReleaseAnalysisData();
                    }
                    catch (Exception e)
                    {
                        Logger.ErrorNewline("Method threw an exception while dumping - " + e.ToString());
                    }
                }
            });
        }
    }

    private string GenerateGraphTitle(MethodAnalysisContext context)
    {
        StringBuilder stringBuilder = new StringBuilder();
        stringBuilder.Append("Type: ");
        stringBuilder.Append(context.DeclaringType?.FullName);
        stringBuilder.Append("\n");
        stringBuilder.Append("Method: ");
        stringBuilder.Append(context.DefaultName);
        stringBuilder.Append("\n");
        return stringBuilder.ToString();
    }

    public DotGraph GenerateGraph(ISILControlFlowGraph graph, MethodAnalysisContext method)
    {
        var directedGraph = new DotGraph()
            .WithIdentifier("Graph")
            .Directed()
            .WithLabel(GenerateGraphTitle(method));

        var nodeCache = new Dictionary<int, DotNode>();
        var edgeCache = new List<DotEdge>();
        DotNode GetOrAddNode(int id) {
            if (nodeCache.TryGetValue(id, out var node)) {
                return node;
            } 
            var newNode = new DotNode().WithIdentifier(id.ToString());
            directedGraph.Add(newNode);
            nodeCache[id] = newNode;
            return newNode;
        }
        DotEdge GetOrAddEdge(DotNode from, DotNode to) {
            foreach (var edge in edgeCache)
            {
                if (edge.From == from.Identifier && edge.To == to.Identifier) { return edge; }
            }
            var newEdge = new DotEdge()
                .From(from)
                .To(to);
            edgeCache.Add(newEdge);
            directedGraph.Add(newEdge);
            return newEdge;
        }


        foreach (var block in graph.Blocks)
        {
            var node = GetOrAddNode(block.ID);
            if (block.BlockType == BlockType.Entry)
            {
                node.WithColor("green");
                node.WithLabel("Entry point");
            }
            else if (block.BlockType == BlockType.Exit)
            {
                node.WithColor("red");
                node.WithLabel("Exit point");
            }
            else
            {
                node.WithShape("box");
                node.WithLabel(block.ToString());
            }
            foreach (var succ in block.Successors)
            {
                var target = GetOrAddNode(succ.ID);
                GetOrAddEdge(node, target);
            }
        }
        return directedGraph;
    }

    private static string GetFilePathForMethod(string outputRoot, MethodAnalysisContext method, string assemblyNameClean)
    {
        TypeAnalysisContext type = method.DeclaringType;



        //Get root assembly directory
        var assemblyDir = Path.Combine(outputRoot, assemblyNameClean);

        //If type is nested, we should use namespace of ultimate declaring type, which could be an arbitrary depth
        //E.g. rewired has a type Rewired.Data.Mapping.HardwareJoystickMap, which contains a nested class Platform_Linux_Base, which contains MatchingCriteria, which contains ElementCount.
        var ultimateDeclaringType = type;
        while (ultimateDeclaringType.DeclaringType != null)
            ultimateDeclaringType = ultimateDeclaringType.DeclaringType;

        var namespaceSplit = ultimateDeclaringType.Namespace!.Split('.');
        namespaceSplit = namespaceSplit
            .Peek(n => MiscUtils.InvalidPathChars.ForEach(c => n = n.Replace(c, '_')))
            .Select(n => MiscUtils.InvalidPathElements.Contains(n) ? $"__illegalwin32name_{n}__" : n)
            .ToArray();

        //Ok so we have the namespace directory. Now we need to join all the declaring type hierarchy together for a filename.
        var declaringTypeHierarchy = new List<string>();
        var declaringType = type.DeclaringType;
        while (declaringType != null)
        {
            declaringTypeHierarchy.Add(declaringType.Name!);
            declaringType = declaringType.DeclaringType;
        }

        //Reverse so we have top-level type first.
        declaringTypeHierarchy.Reverse();

        //Join the hierarchy together with _NestedType_ separators
        string filename;
        if (declaringTypeHierarchy.Count > 0)
            filename = $"{string.Join("_NestedType_", declaringTypeHierarchy)}_NestedType_{type.Name}";
        else
            filename = $"{type.Name}";

        //Get directory from assembly root + namespace
        var directory = Path.Combine(namespaceSplit.Prepend(assemblyDir).ToArray());
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        //Clean up the filename
        MiscUtils.InvalidPathChars.ForEach(c => filename = filename.Replace(c, '_'));

        filename = Path.Combine(directory, filename);

        if (!Directory.Exists(filename))
            Directory.CreateDirectory(filename);



        var methodFileName = method.DefaultName;

        var parameters = string.Join("_", method.Parameters.Select(p => p.ParameterTypeContext.Name));
        if (parameters.Length > 0)
        {
            methodFileName += "_";
            methodFileName += parameters;
        }
        methodFileName += ".dot";

        MiscUtils.InvalidPathChars.ForEach(c => methodFileName = methodFileName.Replace(c, '_'));



        //Combine the directory and filename
        return Path.Combine(filename, methodFileName);
    }

    private static async Task WriteMethodGraph(string outputRoot, MethodAnalysisContext method, DotGraph graph, string assemblyNameClean)
    {
        var file = GetFilePathForMethod(outputRoot, method, assemblyNameClean);

        // Library doesn't provide a non async option :(
        await using var writer = new StringWriter();
        var context = new CompilationContext(writer, new CompilationOptions());
        await graph.CompileAsync(context);
        var result = writer.GetStringBuilder().ToString();

        // Save it to a file
        File.WriteAllText(file, result);
    }
}
