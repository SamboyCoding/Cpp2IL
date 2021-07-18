using LibCpp2IL.Logging;

namespace Cpp2IL.Core
{
    public class LibLogWriter : LogWriter
    {
        public override void Info(string message)
        {
            Logger.Write("Info", "Library", $"{message}", Logger.INFO);
        }

        public override void Verbose(string message)
        {
            Logger.Write("Verb", "Library", $"{message}", Logger.VERB);
        }
    }
}