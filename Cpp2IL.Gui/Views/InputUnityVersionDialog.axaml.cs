using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Cpp2IL.Gui.ViewModels;

namespace Cpp2IL.Gui.Views
{
    public partial class InputUnityVersionDialog : Window
    {
        private InputUnityVersionViewModel _viewModel;
        public InputUnityVersionDialog()
        {
            DataContext = _viewModel = new();
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }
        
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OkClick(object? sender, RoutedEventArgs e) => Close(_viewModel.Version);
    }
}
