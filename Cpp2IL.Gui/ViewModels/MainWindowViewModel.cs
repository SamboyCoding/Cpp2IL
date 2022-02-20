using System;
using System.Threading;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using Cpp2IL.Core;
using Cpp2IL.Core.Model.Contexts;
using Cpp2IL.Gui.Models;
using Cpp2IL.Gui.Views;
using LibCpp2IL;
using ReactiveUI;

namespace Cpp2IL.Gui.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public MainWindow Window;
        
        private string _statusText = "Drop an IL2CPP game on this window to start, or click here to open a file browser.";
        private bool _hasGame = false;
        private FileTreeEntry _rootNode;
        private MethodBodyMode _methodBodyMode = MethodBodyMode.Isil;
        private TextDocument _editorText = new TextDocument("Select a class to open");

        public TypeAnalysisContext LastSelectedType { get; set; }

        public string StatusText
        {
            get => _statusText;
            set => this.RaiseAndSetIfChanged(ref _statusText, value);
        }

        public bool HasGame
        {
            get => _hasGame;
            set => this.RaiseAndSetIfChanged(ref _hasGame, value);
        }

        public FileTreeEntry RootNode
        {
            get => _rootNode;
            set => this.RaiseAndSetIfChanged(ref _rootNode, value);
        }

        public MethodBodyMode MethodBodyMode
        {
            get => _methodBodyMode;
            set
            {
                this.RaiseAndSetIfChanged(ref _methodBodyMode, value); 
                Console.WriteLine($"MethodBodyMode changed to {value}");
                UpdateEditor();
            }
        }

        public TextDocument EditorText
        {
            get => _editorText;
            set => this.RaiseAndSetIfChanged(ref _editorText, value);
        }

        public async void OnDropped(string[]? droppedFiles)
        {
            if(droppedFiles == null)
                return;

            var dg = DroppedGame.ForPaths(droppedFiles);

            if (dg == null)
            {
                StatusText = "No game found in what you just dropped";
                return;
            }

            var version = dg.UnityVersion;
            while (!version.HasValue)
            {
                Console.WriteLine("Prompting for Unity version");
                var dialog = new InputUnityVersionDialog();
                var inputVersion = await dialog.ShowDialog<string>(Window);

                try
                {
                    version = UnityVersion.Parse(inputVersion);
                }
                catch (Exception)
                {
                    version = null;
                }
            }

            ContinueLoading(dg, version.Value);
        }

        private void OnLoadComplete()
        {
            StatusText = "Building file tree...";

            RootNode = new(Cpp2IlApi.CurrentAppContext!);

            StatusText = "Load complete";
            HasGame = true;
        }

        private void OnLoadFailed(Exception exception)
        {
            StatusText = $"Load failed: {exception}";
        }

        private void ContinueLoading(DroppedGame droppedGame, UnityVersion version)
        {
            var versionArray = new[]{version.Major, version.Minor, version.Build};
         
            StatusText = "Loading game...";

            new Thread(() =>
            {
                try
                {
                    Cpp2IlApi.InitializeLibCpp2Il(droppedGame.BinaryBytes, droppedGame.MetadataBytes, versionArray);
                }
                catch (Exception e)
                {
                    Dispatcher.UIThread.Post(() => OnLoadFailed(e));
                    return;
                }

                Dispatcher.UIThread.Post(OnLoadComplete);
            })
            {
                Name = "Game Loader",
                IsBackground = true
            }.Start();
        }

        public void OnItemSelected(FileTreeEntry fileTreeEntry)
        {
            if(fileTreeEntry.Context is not TypeAnalysisContext type)
                return;

            LastSelectedType = type;
            UpdateEditor();
        }

        private void UpdateEditor()
        {
            EditorText = new(ClassFileBuilder.BuildCsFileForType(LastSelectedType, MethodBodyMode));
        }
    }
}