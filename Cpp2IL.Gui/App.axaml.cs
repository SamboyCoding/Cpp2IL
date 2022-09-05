using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cpp2IL.Core.Logging;
using Cpp2IL.Gui.ViewModels;
using Cpp2IL.Gui.Views;

namespace Cpp2IL.Gui
{
    public class App : Application
    {
        public override void Initialize()
        {
            Logger.InfoNewline("Loading XAML...", "GUI");
            AvaloniaXamlLoader.Load(this);
            Logger.InfoNewline("Loaded XAML.", "GUI");
        }

        public override void OnFrameworkInitializationCompleted()
        {
            Logger.InfoNewline("Framework init complete, configuring logging sink and window", "GUI");
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
