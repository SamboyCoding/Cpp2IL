using System;

namespace Cpp2IL.Core
{
    public static class Logger
    {
	    public delegate void LogEvent(string message, string source);

	    public static event LogEvent VerboseLog;
	    public static event LogEvent InfoLog;
	    public static event LogEvent WarningLog;
	    public static event LogEvent ErrorLog;

        public static void VerboseNewline(string message, string source = "Program") => Verbose($"{message}{Environment.NewLine}", source);

        public static void Verbose(string message, string source = "Program")
        {
            VerboseLog?.Invoke(message, source);
        }
        
        public static void InfoNewline(string message, string source = "Program") => Info($"{message}{Environment.NewLine}", source);

        public static void Info(string message, string source = "Program")
        {
            InfoLog?.Invoke(message, source);
        }
        
        public static void WarnNewline(string message, string source = "Program") => Warn($"{message}{Environment.NewLine}", source);

        public static void Warn(string message, string source = "Program")
        {
	        WarningLog?.Invoke(message, source);
        }
        
        public static void ErrorNewline(string message, string source = "Program") => Error($"{message}{Environment.NewLine}", source);

        public static void Error(string message, string source = "Program")
        {
            ErrorLog?.Invoke(message, source);
        }
    }
}