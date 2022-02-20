using System;

namespace Cpp2IL.Core;

public class SimpleConsoleLogger
{
    public static bool ShowVerbose { private get; set; }

    private static bool LastNoNewline;

    public static void Initialize()
    {
        Logger.InfoLog += (message, source) => Write("Info", source, message);
        Logger.WarningLog += (message, source) => Write("Warn", source, message);
        Logger.ErrorLog += (message, source) => Write("Fail", source, message);

        Logger.VerboseLog += (message, source) =>
        {
            if (ShowVerbose)
                Write("Verb", source, message);
        };
    }

    internal static void Write(string level, string source, string message)
    {
        if (!LastNoNewline)
            WritePrelude(level, source);

        LastNoNewline = !message.EndsWith("\n");

        Console.Write(message);
    }

    private static void WritePrelude(string level, string source)
    {
        var message = $"[{level}] [{source}] ";
            
        Console.Write(message);
    }
}