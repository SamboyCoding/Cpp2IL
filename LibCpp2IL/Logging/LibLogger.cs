using System;

namespace LibCpp2IL.Logging;

public static class LibLogger
{
    public static LogWriter Writer = new DefaultWriter();

    public static bool ShowVerbose { private get; set; } = true;

    internal static void InfoNewline(string message)
    {
        Info($"{message}{Environment.NewLine}");
    }

    internal static void Info(string message)
    {
        Writer.Info(message);
    }

    internal static void WarnNewline(string message)
    {
        Warn($"{message}{Environment.NewLine}");
    }

    internal static void Warn(string message)
    {
        Writer.Warn(message);
    }

    internal static void ErrorNewline(string message)
    {
        Error($"{message}{Environment.NewLine}");
    }

    internal static void Error(string message)
    {
        Writer.Error(message);
    }

    internal static void VerboseNewline(string message)
    {
        Verbose($"{message}{Environment.NewLine}");
    }

    internal static void Verbose(string message)
    {
        if (ShowVerbose)
            Writer.Verbose(message);
    }
}
