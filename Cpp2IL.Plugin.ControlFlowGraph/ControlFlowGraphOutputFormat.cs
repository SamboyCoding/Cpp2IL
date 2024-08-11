using Cpp2IL.Core.Api;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;
using Cpp2IL.Core.Extensions;
using Rubjerg.Graphviz;
using System.Text;
using Cpp2IL.Core.Graphs;

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

            MiscUtils.ExecuteParallel(assembly.Types, type =>
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

                        WriteMethodGraph(outputRoot, method, GenerateGraph(method.ControlFlowGraph, method), assemblyNameClean);

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

    public RootGraph GenerateGraph(ISILControlFlowGraph graph, MethodAnalysisContext method)
    {

        RootGraph root = RootGraph.CreateNew(GraphType.Directed, "Graph");
        root.SetAttribute("label", GenerateGraphTitle(method));
        foreach (var block in graph.Blocks)
        {
            Node node = root.GetOrAddNode(block.ID.ToString());
            if (block.BlockType == BlockType.Entry)
            {
                node.SetAttribute("color", "green");
                node.SetAttribute("label", "Entry point");
            }
            else if (block.BlockType == BlockType.Exit)
            {
                node.SetAttribute("color", "red");
                node.SetAttribute("label", "Exit point");
            }
            else
            {
                node.SetAttribute("shape", "box");
                node.SetAttribute("label", block.ToString());
            }
            foreach (var succ in block.Successors)
            {
                var target = root.GetOrAddNode(succ.ID.ToString());
                Edge edge = root.GetOrAddEdge(node, target);
            }
        }
        return root;
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
        methodFileName += ".png";

        MiscUtils.InvalidPathChars.ForEach(c => methodFileName = methodFileName.Replace(c, '_'));



        //Combine the directory and filename
        return Path.Combine(filename, methodFileName);
    }

    private static void WriteMethodGraph(string outputRoot, MethodAnalysisContext method, RootGraph rootGraph, string assemblyNameClean)
    {
        var file = GetFilePathForMethod(outputRoot, method, assemblyNameClean);

        rootGraph.ToPngFile(file);
    }
}
