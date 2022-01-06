using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Cpp2IL.Gui.ViewModels;

namespace Cpp2IL.Gui.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public void OnCreated()
        {
            var vm = (MainWindowViewModel) DataContext!;
            vm.Window = this;
            AddHandler(DragDrop.DropEvent, (sender, args) => vm.OnDropped(args.Data.GetFileNames()?.ToArray()));
        }

        private async void OnClickDropPrompt(object? sender, PointerPressedEventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                AllowMultiple = true
            };
            var ret = await dialog.ShowAsync(this);
            var vm = (MainWindowViewModel) DataContext!;
            vm.OnDropped(ret);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}