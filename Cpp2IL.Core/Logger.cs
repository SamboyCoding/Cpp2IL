using System;

namespace Cpp2IL.Core
{
    public static class Logger
    {
        public delegate void LogEvent(string message, string source);

        public static event LogEvent VerboseLog = (_, _) => {};
        public static event LogEvent InfoLog = (_, _) => { };
        public static event LogEvent WarningLog = (_, _) => { };
        public static event LogEvent ErrorLog = (_, _) => { };

        public static void VerboseNewline(string message, string source = "Program") => Verbose($"{message}{Environment.NewLine}", source);

        public static void Verbose(string message, string source = "Program")
        {
            VerboseLog(message, source);
        }
        
        public static void InfoNewline(string message, string source = "Program") => Info($"{message}{Environment.NewLine}", source);

        public static void Info(string message, string source = "Program")
        {
            InfoLog(message, source);
        }
        
        public static void WarnNewline(string message, string source = "Program") => Warn($"{message}{Environment.NewLine}", source);

        public static void Warn(string message, string source = "Program")
        {
            WarningLog(message, source);
        }
        
        public static void ErrorNewline(string message, string source = "Program") => Error($"{message}{Environment.NewLine}", source);

        public static void Error(string message, string source = "Program")
        {
            ErrorLog(message, source);
        }
    }
}