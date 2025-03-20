using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AssetRipper.CIL;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Core.Utils;
using Cpp2IL.Core.Utils.AsmResolver;
using Decompiler;
using Decompiler.ControlFlow;
using Decompiler.IL;
using LibCpp2IL;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AsmResolver.PE.DotNet.Metadata.Tables;
using Logger = Cpp2IL.Core.Logging.Logger;

namespace Cpp2IL.Core.OutputFormats;

public class IlOutputFormat : AsmResolverDllOutputFormat
{
    private class Context : IContext
    {
        public AssemblyAnalysisContext? Assembly;
        public ApplicationAnalysisContext? App;
        public ModuleDefinition? Module;
        public MethodAnalysisContext? Method;

        public TypeDefinition? GetTypeByAddress(ulong address)
        {
            if (Method!.DeclaringType == null)
                return null;

            try
            {
                var type = LibCpp2IlMain.GetTypeGlobalByAddress(address);
                var typeContext = type?.ToContext(Assembly!);
                return typeContext?.ToTypeSignature(Module!).Resolve();
            }
            catch (Exception e)
            {
                return null;
            }
        }

        public FieldDefinition? GetFieldByOffset(TypeDefinition type, long offset)
        {
            var importer = type.Module!.DefaultImporter;

            try // I don't know how to do this correctly while ignoring all of those weird il2cpp fields (like cctor_finished_or_no_cctor) so ill just use first field for now
            {
                var typeContext = App!.AllTypes.First(t => t.FullName == type.FullName);

                if (typeContext.Methods.Count == 0)
                    return null;

                var managedMethod = typeContext.Methods[0].GetExtraData<MethodDefinition>("AsmResolverMethod")!;
                var managedType = managedMethod.DeclaringType!;

                if (managedType.Fields.Count == 0)
                    return null;

                return managedType.Fields[0];
            }
            catch (Exception e)
            {
                return null;
            }
        }
    }

    public override string OutputFormatId => "il";
    public override string OutputFormatName => ".NET IL";

    private static string[] _namespacesToSkip = ["UnityEngine.", "Unity.", "Mono.", "System.", "TMPro.", "Newtonsoft."];
    private static int _maxMethodSize = 50_000;

    public override List<AssemblyDefinition> BuildAssemblies(ApplicationAnalysisContext context)
    {
#if VERBOSE_LOGGING
        var asmCount = context.Assemblies.Count;
        var typeCount = context.AllTypes.Count();
        var methodCount = context.AllTypes.SelectMany(t => t.Methods).Count();
        var fieldCount = context.AllTypes.SelectMany(t => t.Fields).Count();
        var propertyCount = context.AllTypes.SelectMany(t => t.Properties).Count();
        var eventCount = context.AllTypes.SelectMany(t => t.Events).Count();
#endif

        //Build the stub assemblies
        var start = DateTime.Now;
#if VERBOSE_LOGGING
        Logger.Verbose($"Building stub assemblies ({asmCount} assemblies, {typeCount} types)...", "DllOutput");
#else
        Logger.Verbose($"Building stub assemblies...", "DllOutput");
#endif
        List<AssemblyDefinition> ret = BuildStubAssemblies(context);
        Logger.VerboseNewline($"{(DateTime.Now - start).TotalMilliseconds:F1}ms", "DllOutput");

        start = DateTime.Now;
        Logger.Verbose("Configuring inheritance and generics...", "DllOutput");

        Parallel.ForEach(context.Assemblies, AsmResolverAssemblyPopulator.ConfigureHierarchy);

        Logger.VerboseNewline($"{(DateTime.Now - start).TotalMilliseconds:F1}ms", "DllOutput");

        //Populate them
        start = DateTime.Now;

#if VERBOSE_LOGGING
        Logger.Verbose($"Adding {fieldCount} fields, {methodCount} methods, {propertyCount} properties, and {eventCount} events (in parallel)...", "DllOutput");
#else
        Logger.Verbose($"Adding fields, methods, properties, and events (in parallel)...", "DllOutput");
#endif

        MiscUtils.ExecuteParallel(context.Assemblies, AsmResolverAssemblyPopulator.CopyDataFromIl2CppToManaged);
        MiscUtils.ExecuteParallel(context.Assemblies, AsmResolverAssemblyPopulator.AddExplicitInterfaceImplementations);

        var methods = new List<MethodAnalysisContext>();

        foreach (var assembly in context.Assemblies)
        {
            foreach (var type in assembly.Types)
            {
                if (_namespacesToSkip.Any(n => type.FullName.StartsWith(n)))
                    continue;

                methods.AddRange(type.Methods);
            }
        }

        var totalCount = methods.Count;
        var processedCount = 0;

        // Non parallel version (for debugging)
        /*var decompiler = new IlDecompiler();

        foreach (var method in methods)
        {
            var managedMethod = method.GetExtraData<MethodDefinition>("AsmResolverMethod")!;

            FillMethodBody(managedMethod, method, decompiler);
            processedCount++;

            var progress = (float)processedCount / totalCount;

            var name = managedMethod.FullName;
            if (name.Length > 100)
                name = name[..100] + "...";

            var status = $"Decompiling {name}";
            PrintProgressBar(progress, status);
        }*/

        var decompilers = new ConcurrentBag<IlDecompiler>(Enumerable.Range(0, 70).Select(_ => new IlDecompiler()));

        Parallel.ForEach(methods, method =>
        {
            if (decompilers.TryTake(out var decompiler))
            {
                var managedMethod = method.GetExtraData<MethodDefinition>("AsmResolverMethod")!;

                FillMethodBody(managedMethod, method, decompiler);
                Interlocked.Increment(ref processedCount);

                var progress = (float)processedCount / totalCount;

                var name = managedMethod.FullName;
                if (name.Length > 100)
                    name = name[..100] + "...";

                var status = $"Decompiling {name}";
                PrintProgressBar(progress, status);

                decompilers.Add(decompiler);
            }
        });

        PrintProgressBar(1, "Done!");
        Console.WriteLine();

        Logger.VerboseNewline($"{(DateTime.Now - start).TotalMilliseconds:F1}ms", "DllOutput");

        //Populate custom attributes
        start = DateTime.Now;
        Logger.Verbose("Adding custom attributes to all of the above...", "DllOutput");
        MiscUtils.ExecuteParallel(context.Assemblies, AsmResolverAssemblyPopulator.PopulateCustomAttributes);

        Logger.VerboseNewline($"{(DateTime.Now - start).TotalMilliseconds:F1}ms", "DllOutput");

        TypeDefinitionsAsmResolver.Reset();

        return ret;
    }

    protected override void FillMethodBody(MethodDefinition methodDefinition, MethodAnalysisContext methodContext)
    {
    }

    private void FillMethodBody(MethodDefinition methodDefinition, MethodAnalysisContext methodContext, IlDecompiler decompiler)
    {
        var context = new Context { App = methodContext.AppContext, Module = methodDefinition.Module, Assembly = methodContext.DeclaringType!.DeclaringAssembly, Method = methodContext };

        if (!methodDefinition.IsManagedMethodWithBody()) return;
        methodDefinition.CilMethodBody = new CilMethodBody(methodDefinition);

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
            decompiler.Decompile(method, context);

            //var outputPath = Path.Combine(OutputPath, "CFG-Output");
            //WriteControlFlowGraph(method.ControlFlowGraph, methodContext, methodDefinition, method, outputPath);
        }
        catch (LimitReachedException e)
        {
            Logger.WarnNewline(methodContext.FullName + ": " + e.Message, "IlOutputFormat");
        }
        catch (Exception e)
        {
            Logger.ErrorNewline(methodContext.FullName + ": " + e.Message, "IlOutputFormat");
            IlDecompiler.ReplaceBodyWithException(methodDefinition, e.ToString());
        }
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
