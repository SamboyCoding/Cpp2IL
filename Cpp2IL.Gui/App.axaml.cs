using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cpp2IL.Gui.ViewModels;
using Cpp2IL.Gui.Views;

namespace Cpp2IL.Gui
{
    public class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            Avalonia.Logging.Logger.Sink = new ConsoleAvaloniaSink();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(),
                };
                ((MainWindow) desktop.MainWindow).OnCreated();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}