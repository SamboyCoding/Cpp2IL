using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Cpp2IL.Core.Logging;
using Pastel;

namespace Cpp2IL;

internal static class ConsoleLogger
{
    internal static readonly Color VERB = Color.Gray;
    internal static readonly Color INFO = Color.LightBlue;
    internal static readonly Color WARN = Color.Yellow;
    internal static readonly Color ERROR = Color.DarkRed;

    internal static bool DisableColor { private get; set; }

    internal static bool ShowVerbose { private get; set; }

    private static bool LastNoNewline;

    public static void Initialize()
    {
        Logger.InfoLog += (message, source) => Write("Info", source, message, INFO);
        Logger.WarningLog += (message, source) => Write("Warn", source, message, WARN);
        Logger.ErrorLog += (message, source) => Write("Fail", source, message, ERROR);

        Logger.VerboseLog += (message, source) =>
        {
            if (ShowVerbose)
                Write("Verb", source, message, VERB);
        };

        CheckColorSupport();
    }

    internal static void Write(string level, string source, string message, Color color)
    {
        if (!LastNoNewline)
            WritePrelude(level, source, color);

        LastNoNewline = message[^1] != '\n';

        if (!DisableColor)
            message = message.Pastel(color);

        Console.Write(message);
    }

    private static void WritePrelude(string level, string source, Color color)
    {
        var message = $"[{level}] [{source}] ";
        if (!DisableColor)
            message = message.Pastel(color);

        Console.Write(message);
    }

    public static void CheckColorSupport()
    {
        // if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        // {
        //     DisableColor = true;
        //     WarnNewline("Looks like you're running on a non-windows platform. Disabling ANSI color codes.");
        // }
        /*else*/
        if (CheckWine())
        {
            DisableColor = true;
            Logger.WarnNewline("Looks like you're running in wine or proton. Disabling ANSI color codes.");
        }
        else if (Environment.GetEnvironmentVariable("NO_COLOR") != null)
        {
            DisableColor = true; //Just manually set this, even though Pastel respects the environment variable
            Logger.WarnNewline("NO_COLOR set, disabling ANSI color codes as you requested.");
        }
        else
        {
            //Ensure we run the cctor for Pastel now.
            ConsoleExtensions.Enable();
        }
    }

    private static bool CheckWine()
    {
#if NET472
        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            return false;

        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);

        return GetProcAddress(GetModuleHandle("ntdll.dll"), "wine_get_version") != IntPtr.Zero;
#else
        if(!OperatingSystem.IsWindows())
            return false;
        
        return NativeLibrary.TryGetExport(NativeLibrary.Load("ntdll.dll"), "wine_get_version", out _);
#endif
    }
}
