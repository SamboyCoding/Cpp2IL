using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using AvaloniaEdit;
using AvaloniaEdit.TextMate;
using Cpp2IL.Core.Logging;
using Cpp2IL.Gui.Models;
using Cpp2IL.Gui.ViewModels;
using TextMateSharp.Grammars;

#if DEBUG
using Avalonia;
#endif

namespace Cpp2IL.Gui.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            Logger.InfoNewline("[MainWindow] Initializing components...", "GUI");
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public void OnCreated()
        {
            Logger.InfoNewline("[MainWindow] OnCreated", "GUI");
            var vm = (MainWindowViewModel) DataContext!;
            vm.Window = this;
            
            var textEditor = this.FindControl<TextEditor>("CodeView");

            var registryOptions = new RegistryOptions(ThemeName.DarkPlus);
            var textMateInstallation = textEditor.InstallTextMate(registryOptions);
            textMateInstallation.SetGrammar(registryOptions.GetScopeByLanguageId(registryOptions.GetLanguageByExtension(".cs").Id));
            
            // This assumes nothing else is passed to the command line as it is a GUI build!
            var commandLine = System.Environment.GetCommandLineArgs();
            if (commandLine != null && commandLine.Length > 1) {
                vm.OnDropped(commandLine.Skip(1).ToArray());
            }
            AddHandler(DragDrop.DropEvent, (sender, args) => vm.OnDropped(args.Data.GetFileNames()?.ToArray()));
        }

        private async void OnClickDropPrompt(object? sender, PointerPressedEventArgs e)
        {
            var ret = await StorageProvider.OpenFilePickerAsync(new() { AllowMultiple = true, });
            var vm = (MainWindowViewModel) DataContext!;
            
            var paths = new List<string>();
            foreach (var file in ret)
            {
                paths.Add(file.Path.LocalPath);
            }

            vm.OnDropped(paths.ToArray());
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void SelectingItemsControl_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if(e.AddedItems.Count < 1)
                return;
            
            if(e.AddedItems[0] is not FileTreeEntry fte)
                return;

            ((MainWindowViewModel) DataContext!).OnItemSelected(fte);
        }
    }
}
