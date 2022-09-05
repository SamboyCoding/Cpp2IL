using System;
using Avalonia;
using Avalonia.ReactiveUI;
using Cpp2IL.Core;
using Cpp2IL.Core.Logging;

namespace Cpp2IL.Gui
{
    class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            Console.WriteLine("Starting Cpp2IL GUI. Initializing Cpp2IL Core...");
            
            Cpp2IlApi.Init();
            SimpleConsoleLogger.Initialize();
            SimpleConsoleLogger.ShowVerbose = true;
            // Trace.Listeners.Add(new TextWriterTraceListener(Console.Out));
            
            Logger.InfoNewline("Starting Avalonia...", "GUI");
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI();
    }
}
