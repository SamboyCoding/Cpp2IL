using Cpp2IL.Core.Logging;

namespace Cpp2IL.Core.Api;

public sealed class PluginLogger
{
    private readonly Cpp2IlPlugin _plugin;
    private readonly string _name;

    internal PluginLogger(Cpp2IlPlugin plugin)
    {
        _plugin = plugin;
        _name = $"Plugin: {plugin.Name}";
    }

    public void VerboseNewline(string message, string source = "Program") => Logger.VerboseNewline($"{message}", _name);

    public void Verbose(string message, string source = "Program") => Logger.Verbose($"{message}", _name);

    public void InfoNewline(string message, string source = "Program") => Logger.InfoNewline($"{message}", _name);

    public void Info(string message, string source = "Program") => Logger.Info($"{message}", _name);

    public void WarnNewline(string message, string source = "Program") => Logger.WarnNewline($"{message}", _name);

    public void Warn(string message, string source = "Program") => Logger.Warn($"{message}", _name);

    public void ErrorNewline(string message, string source = "Program") => Logger.ErrorNewline($"{message}", _name);

    public void Error(string message, string source = "Program") => Logger.Error($"{message}", _name);
}
