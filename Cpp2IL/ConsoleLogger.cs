using System;
using System.Drawing;
#if Windows
using System.Runtime.InteropServices;
#endif
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

#if Windows
        // https://devblogs.microsoft.com/oldnewthing/20120514-00/?p=7633
        // https://github.com/lain804/winedetect/blob/5f622df8cda9b26b4a1e8ded7171e281aae85a13/wine%20vibe%20check/main.cpp#L9-L11
        if (Kernel32.MulDiv(1, int.MinValue, int.MinValue) != -1)
        {
            DisableColor = true;
            Logger.WarnNewline("Looks like you're running in wine or proton. Disabling ANSI color codes.");
        }
        else
#endif
        if (Environment.GetEnvironmentVariable("NO_COLOR") != null)
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
}

#if Windows
file class Kernel32
{
    [DllImport("kernel32.dll")]
    public static extern int MulDiv(int nNumber, int nNumerator, int nDenominator);
}
#endif
