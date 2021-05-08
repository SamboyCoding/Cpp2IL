namespace LibCpp2IL.Logging
{
    public abstract class LogWriter
    {
        public abstract void Info(string message);
        public abstract void Verbose(string message);
    }
}