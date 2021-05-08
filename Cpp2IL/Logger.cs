using System;

namespace Cpp2IL
{
    internal static class Logger
    {
        internal static bool ShowVerbose { private get; set; }
        
        private static bool LastNoNewline;

        public static void VerboseNewline(string message, string source = "Program") => Verbose($"{message}\n", source);

        public static void Verbose(string message, string source = "Program")
        {
            if(ShowVerbose)
                Write("Verb", source, message);
        }
        
        public static void InfoNewline(string message, string source = "Program") => Info($"{message}\n", source);

        public static void Info(string message, string source = "Program")
        {
            Write("Info", source, message);
        }
        
        public static void WarnNewline(string message, string source = "Program") => Warn($"{message}\n", source);

        public static void Warn(string message, string source = "Program")
        {
            Write("Warn", source, message);
        }

        internal static void Write(string level, string source, string message)
        {
            WritePrelude(level, source);
            Console.Write(message);

            LastNoNewline = !message.EndsWith('\n');
        }

        private static void WritePrelude(string level, string source)
        {
            if(!LastNoNewline)
                Console.Write($"[{level}] [{source}] ");
        }
    }
}