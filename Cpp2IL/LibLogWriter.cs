using LibCpp2IL.Logging;

namespace Cpp2IL
{
    public class LibLogWriter : LogWriter
    {
        public override void Info(string message)
        {
            Logger.Write("Info", "Library", $"{message}");
        }

        public override void Verbose(string message)
        {
            Logger.Write("Verb", "Library", $"{message}");
        }
    }
}