namespace LibCpp2IL.Logging
{
    public static class LibLogger
    {
        public static LogWriter Writer = new DefaultWriter();

        public static bool ShowVerbose { private get; set; } = true;

        internal static void InfoNewline(string message)
        {
            Info($"{message}\n");
        }

        internal static void Info(string message)
        {
            Writer.Info(message);
        }

        internal static void VerboseNewline(string message)
        {
            Verbose($"{message}\n");
        }

        internal static void Verbose(string message)
        {
            if (ShowVerbose)
                Writer.Verbose(message);
        }
    }
}