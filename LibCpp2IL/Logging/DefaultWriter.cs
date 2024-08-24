using System;

namespace LibCpp2IL.Logging;

public class DefaultWriter : LogWriter
{
    public override void Info(string message)
    {
        Console.Write(message);
    }

    public override void Warn(string message)
    {
        Console.Write(message);
    }

    public override void Error(string message)
    {
        Console.Write(message);
    }

    public override void Verbose(string message)
    {
        Console.Write(message);
    }
}
