using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime;
using CommandLine;
using Cpp2IL.Core;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;
#if !DEBUG
using Cpp2IL.Core.Exceptions;
#endif
using LibCpp2IL.Wasm;
using AssetRipper.Primitives;
using Cpp2IL.Core.Extensions;

#if NET472
using LibCpp2IL;
#endif

namespace Cpp2IL;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
internal class Program
{
    public static readonly string Cpp2IlVersionString = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

#if !NETFRAMEWORK
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, "Cpp2IL.CommandLineArgs", "Cpp2IL")]
#endif
    private static Cpp2IlRuntimeArgs GetRuntimeOptionsFromCommandLine(string[] commandLine)
    {
        var parserResult = Parser.Default.ParseArguments<CommandLineArgs>(commandLine);

        if (parserResult is NotParsed<CommandLineArgs> notParsed && notParsed.Errors.Count() == 1 && notParsed.Errors.All(e => e.Tag is ErrorType.VersionRequestedError or ErrorType.HelpRequestedError))
            //Version or help requested
            Environment.Exit(0);

        if (parserResult is not Parsed<CommandLineArgs> { Value: { } options })
            throw new SoftException("Failed to parse command line arguments");

        ConsoleLogger.ShowVerbose = options.Verbose;

#pragma warning disable IL2026 // RequiresUnreferencedCode
        Cpp2IlApi.Init();
#pragma warning restore IL2026

        if (options.ListProcessors)
        {
            Logger.InfoNewline("Available processors:");
            foreach (var cpp2IlProcessingLayer in ProcessingLayerRegistry.AllProcessingLayers)
                Console.WriteLine($"  ID: {cpp2IlProcessingLayer.Id}   Name: {cpp2IlProcessingLayer.Name}");
            Environment.Exit(0);
        }

        if (options.ListOutputFormats)
        {
            Logger.InfoNewline("Available output formats:");
            foreach (var cpp2IlOutputFormat in OutputFormatRegistry.AllOutputFormats)
                Console.WriteLine($"  ID: {cpp2IlOutputFormat.OutputFormatId}   Name: {cpp2IlOutputFormat.OutputFormatName}");
            Environment.Exit(0);
        }

        if (!options.AreForceOptionsValid)
            throw new SoftException("Invalid force option configuration");

        Cpp2IlApi.ConfigureLib(false);

        var result = new Cpp2IlRuntimeArgs();

        if (options.ForcedBinaryPath == null)
        {
#if !NET472
            if (options.GamePath != null && options.GamePath.StartsWith('~'))
                options.GamePath = string.Concat(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), options.GamePath.AsSpan(1));
#else
            if (options.GamePath != null && options.GamePath.StartsWith("~"))
                options.GamePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + options.GamePath[1..];
#endif
                
            PathUtils.ResolvePathsFromCommandLine(options.GamePath, options.ExeName, ref result);
        }
        else
        {
            Logger.WarnNewline("Using force options, I sure hope you know what you're doing!");
            result.PathToAssembly = options.ForcedBinaryPath!;
            result.PathToMetadata = options.ForcedMetadataPath!;
            result.UnityVersion = UnityVersion.Parse(options.ForcedUnityVersion!);

            if (result.UnityVersion.Type == UnityVersionType.Alpha && result.UnityVersion.Build == 0)
                //Map a0 to f1 - we assume the user simply didn't provide the final part of the version number
                result.UnityVersion = new UnityVersion(result.UnityVersion.Major, result.UnityVersion.Minor, result.UnityVersion.Build, UnityVersionType.Final, 1);

            result.Valid = true;
        }

        result.WasmFrameworkJsFile = options.WasmFrameworkFilePath;

        result.OutputRootDirectory = options.OutputRootDir;

        result.LowMemoryMode = options.LowMemoryMode;

        // if(string.IsNullOrEmpty(options.OutputFormatId))      // throw new SoftException("No output format specified, so nothing to do!");

        if (!string.IsNullOrEmpty(options.OutputFormatId))
        {
            try
            {
                result.OutputFormat = OutputFormatRegistry.GetFormat(options.OutputFormatId!);
                Logger.VerboseNewline($"Selected output format: {result.OutputFormat.OutputFormatName}");
            }
            catch (Exception e)
            {
                throw new SoftException(e.Message);
            }
        }

        try
        {
            result.ProcessingLayersToRun = options.ProcessorsToUse.Select(ProcessingLayerRegistry.GetById).ToList();
            if (result.ProcessingLayersToRun.Count > 0)
                Logger.VerboseNewline($"Selected processing layers: {string.Join(", ", result.ProcessingLayersToRun.Select(l => l.Name))}");
            else
                Logger.VerboseNewline("No processing layers requested");
        }
        catch (Exception e)
        {
            throw new SoftException(e.Message);
        }

        try
        {
            options.ProcessorConfigOptions.Select(c => c.Split('=')).ToList().ForEach(s => result.ProcessingLayerConfigurationOptions.Add(s[0], s[1]));
        }
        catch (IndexOutOfRangeException)
        {
            throw new SoftException("Processor config options must be in the format 'key=value'");
        }

        return result;
    }

    public static int Main(string[] args)
    {
        Console.WriteLine("===Cpp2IL by Samboy063===");
        Console.WriteLine("A Tool to Reverse Unity's \"il2cpp\" Build Process.");
        Console.WriteLine($"Version {Cpp2IlVersionString}\n");

        ConsoleLogger.Initialize();

        Logger.InfoNewline("Running on " + Environment.OSVersion.Platform);

        try
        {
            var runtimeArgs = GetRuntimeOptionsFromCommandLine(args);

            if (runtimeArgs.LowMemoryMode)
                //Force an early collection for all the zip shenanigans we may have just done
                GC.Collect();

            return MainWithArgs(runtimeArgs);
        }
        catch (SoftException e)
        {
            Logger.ErrorNewline($"Execution Failed: {e.Message}");
            return -1;
        }
#if !DEBUG
            catch (DllSaveException e)
            {
                Logger.ErrorNewline(e.ToString());
                Console.WriteLine();
                Console.WriteLine();
                Logger.ErrorNewline("Waiting for you to press enter - feel free to copy the error...");
                Console.ReadLine();

                return -1;
            }
            catch (LibCpp2ILInitializationException e)
            {
                Logger.ErrorNewline($"\n\n{e}\n\nWaiting for you to press enter - feel free to copy the error...");
                Console.ReadLine();
                return -1;
            }
#endif
    }

    public static int MainWithArgs(Cpp2IlRuntimeArgs runtimeArgs)
    {
        if (!runtimeArgs.Valid)
            throw new SoftException("Arguments have Valid = false");

        Cpp2IlApi.RuntimeOptions = runtimeArgs;

        var executionStart = DateTime.Now;

        runtimeArgs.OutputFormat?.OnOutputFormatSelected();

        GCSettings.LatencyMode = runtimeArgs.LowMemoryMode ? GCLatencyMode.Interactive : GCLatencyMode.SustainedLowLatency;

        if (runtimeArgs.WasmFrameworkJsFile != null)
            try
            {
                var frameworkJs = File.ReadAllText(runtimeArgs.WasmFrameworkJsFile);
                var remaps = WasmUtils.ExtractAndParseDynCallRemaps(frameworkJs);
                Logger.InfoNewline($"Parsed {remaps.Count} dynCall remaps from {runtimeArgs.WasmFrameworkJsFile}");
                WasmFile.RemappedDynCallFunctions = remaps;
            }
            catch (Exception e)
            {
                WasmFile.RemappedDynCallFunctions = null;
                Logger.WarnNewline($"Failed to parse dynCall remaps from Wasm Framework Javascript File: {e}. They will not be used, so you probably won't get method bodies!");
            }
        else
            WasmFile.RemappedDynCallFunctions = null;

        Cpp2IlApi.InitializeLibCpp2Il(runtimeArgs.PathToAssembly, runtimeArgs.PathToMetadata, runtimeArgs.UnityVersion);

        if (runtimeArgs.LowMemoryMode)
            GC.Collect();

        foreach (var (key, value) in runtimeArgs.ProcessingLayerConfigurationOptions)
            Cpp2IlApi.CurrentAppContext.PutExtraData(key, value);

        //Pre-process processing layers, allowing them to stop others from running
        Logger.InfoNewline("Pre-processing processing layers...");
        var layers = runtimeArgs.ProcessingLayersToRun.Clone();
        RunProcessingLayers(runtimeArgs, processingLayer => processingLayer.PreProcess(Cpp2IlApi.CurrentAppContext, layers));
        runtimeArgs.ProcessingLayersToRun = layers;

        //Run processing layers
        Logger.InfoNewline("Invoking processing layers...");
        RunProcessingLayers(runtimeArgs, processingLayer => processingLayer.Process(Cpp2IlApi.CurrentAppContext));

        var outputStart = DateTime.Now;

        if (runtimeArgs.OutputFormat != null)
        {
            if (runtimeArgs.LowMemoryMode)
                GC.Collect();

            Logger.InfoNewline($"Outputting as {runtimeArgs.OutputFormat.OutputFormatName} to {runtimeArgs.OutputRootDirectory}...");
            runtimeArgs.OutputFormat.DoOutput(Cpp2IlApi.CurrentAppContext, runtimeArgs.OutputRootDirectory);
            Logger.InfoNewline($"Finished outputting in {(DateTime.Now - outputStart).TotalMilliseconds}ms");
        }
        else
        {
            Logger.WarnNewline("No output format requested, so not outputting anything. The il2cpp game loaded properly though! (Hint: You probably want to specify an output format, try --output-as)");
        }

        // if (runtimeArgs.EnableMetadataGeneration)
        // Cpp2IlApi.GenerateMetadataForAllAssemblies(runtimeArgs.OutputRootDirectory);

        // if (runtimeArgs.EnableAnalysis)
        // Cpp2IlApi.PopulateConcreteImplementations();

        PathUtils.CleanupExtractedFiles();

        Cpp2IlPluginManager.CallOnFinish();

        Logger.InfoNewline($"Done. Total execution time: {(DateTime.Now - executionStart).TotalMilliseconds}ms");
        return 0;
    }

    private static void RunProcessingLayers(Cpp2IlRuntimeArgs runtimeArgs, Action<Cpp2IlProcessingLayer> run)
    {
        foreach (var processingLayer in runtimeArgs.ProcessingLayersToRun)
        {
            var processorStart = DateTime.Now;

            Logger.InfoNewline($"    {processingLayer.Name}...");

#if !DEBUG
                try
                {
#endif
            run(processingLayer);
#if !DEBUG
                }
                catch (Exception e)
                {
                    Logger.ErrorNewline($"Processing layer {processingLayer.Id} threw an exception: {e}");
                    Environment.Exit(1);
                }
#endif

            if (runtimeArgs.LowMemoryMode)
                GC.Collect();

            Logger.InfoNewline($"    {processingLayer.Name} finished in {(DateTime.Now - processorStart).TotalMilliseconds}ms");
        }
    }
}
