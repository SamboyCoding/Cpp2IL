using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Cpp2IL.Core;
using Cpp2IL.Core.Api;
using Cpp2IL.Core.Extensions;
using Cpp2IL.Core.Logging;
using Cpp2IL.Core.Utils;

namespace Cpp2IL.Gui;

public partial class MainWindow : Window
{
    private Cpp2IlRuntimeArgs _runtimeArgs;

    public MainWindow()
    {
        InitializeComponent();

        _runtimeArgs = new Cpp2IlRuntimeArgs();

        Cpp2IlApi.Init();

        foreach (var outputFormat in OutputFormatRegistry.AllOutputFormats)
        {
            var comboBoxItem = new ComboBoxItem { Content = outputFormat.OutputFormatName };
            OutputFormat.Items.Add(comboBoxItem);
        }

        OutputFormat.SelectedIndex = 0;
    }

    private void Run(object? sender, RoutedEventArgs e)
    {
        Cpp2IlApi.ConfigureLib(false);
        Cpp2IlApi.RuntimeOptions = _runtimeArgs;
        _runtimeArgs.OutputFormat?.OnOutputFormatSelected();
        GCSettings.LatencyMode =
            _runtimeArgs.LowMemoryMode ? GCLatencyMode.Interactive : GCLatencyMode.SustainedLowLatency;
        Cpp2IlApi.InitializeLibCpp2Il(_runtimeArgs.PathToAssembly, _runtimeArgs.PathToMetadata,
            _runtimeArgs.UnityVersion);

        if (_runtimeArgs.LowMemoryMode)
            GC.Collect();

        foreach (var (key, value) in _runtimeArgs.ProcessingLayerConfigurationOptions)
            Cpp2IlApi.CurrentAppContext.PutExtraData(key, value);

        var layers = _runtimeArgs.ProcessingLayersToRun.Clone();
        RunProcessingLayers(_runtimeArgs,
            processingLayer => processingLayer.PreProcess(Cpp2IlApi.CurrentAppContext, layers));
        _runtimeArgs.ProcessingLayersToRun = layers;

        RunProcessingLayers(_runtimeArgs, processingLayer => processingLayer.Process(Cpp2IlApi.CurrentAppContext));

        if (_runtimeArgs.LowMemoryMode)
            GC.Collect();

        _runtimeArgs.OutputFormat.DoOutput(Cpp2IlApi.CurrentAppContext, _runtimeArgs.OutputRootDirectory);

        PathUtils.CleanupExtractedFiles();
        Cpp2IlPluginManager.CallOnFinish();
    }

    private static void RunProcessingLayers(Cpp2IlRuntimeArgs runtimeArgs, Action<Cpp2IlProcessingLayer> run)
    {
        foreach (var processingLayer in runtimeArgs.ProcessingLayersToRun)
        {
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
        }
    }

    private async void ShowOutputFolderBrowser(object? sender, RoutedEventArgs e)
    {
        var path = await ShowFileOrFolderPicker(true, "Select output folder");

        if (string.IsNullOrEmpty(path))
            return;

        _runtimeArgs.OutputRootDirectory = path;
    }

    private void SetOutputFormat(object? sender, AvaloniaPropertyChangedEventArgs avaloniaPropertyChangedEventArgs)
    {
        if (OutputFormat == null || OutputFormat.SelectedIndex < 0)
            return;

        _runtimeArgs.OutputFormat = OutputFormatRegistry.AllOutputFormats[OutputFormat.SelectedIndex];
    }

    private async void ShowFileBrowser(object? sender, RoutedEventArgs e)
    {
        var path = await ShowFileOrFolderPicker(false, "Select game");
        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            PathUtils.ResolvePathsFromCommandLine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path),
                ref _runtimeArgs);
        }
        catch (Exception exception)
        {
            Logger.ErrorNewline(exception.ToString());
            ShowPathResolveError(exception);
            return;
        }

        ShowSettingsTab();
    }

    private async void ShowFolderBrowser(object? sender, RoutedEventArgs e)
    {
        var path = await ShowFileOrFolderPicker(true, "Select game");
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            PathUtils.ResolvePathsFromCommandLine(path, null, ref _runtimeArgs);
        }
        catch (Exception exception)
        {
            Logger.ErrorNewline(exception.ToString());
            ShowPathResolveError(exception);
            return;
        }

        ShowSettingsTab();
    }

    private void ShowSettingsTab() => Tabs.SelectedIndex = 1;

    private void ShowPathResolveError(Exception exception)
    {
        new MessageBox("Failed to load game! Try selecting exe instead of folder.", exception.ToString())
            .ShowDialog(this);
    }

    private async Task<string> ShowFileOrFolderPicker(bool folderPicker, string name)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null)
            throw new Exception("GetTopLevel(this) returned null");

        IReadOnlyList<IStorageItem> item;

        if (folderPicker)
        {
            item = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions()
            {
                AllowMultiple = false, Title = name
            });
        }
        else
        {
            item = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
            {
                AllowMultiple = false, Title = name
            });
        }

        return item.Count == 1 ? item[0].Path.LocalPath : string.Empty;
    }
}
