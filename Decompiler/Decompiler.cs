namespace Decompiler;

/// <summary>
/// The main decompiler class.
/// </summary>
public class Decompiler
{
    /// <summary>
    /// Info log event.
    /// </summary>
    public Action<string, string> InfoLog = (_, _) => { };

    /// <summary>
    /// Warning log event.
    /// </summary>
    public Action<string, string> WarnLog = (_, _) => { };

    /// <summary>
    /// Error log event.
    /// </summary>
    public Action<string, string> ErrorLog = (_, _) => { };

    /// <summary>
    /// Logs info message.
    /// </summary>
    /// <param name="text">Text.</param>
    /// <param name="source">Message source.</param>
    public void Info(string text, string source = "Decompiler") => InfoLog(text, source);

    /// <summary>
    /// Logs warning message.
    /// </summary>
    /// <param name="text">Text.</param>
    /// <param name="source">Message source.</param>
    public void Warn(string text, string source = "Decompiler") => WarnLog(text, source);

    /// <summary>
    /// Logs error message.
    /// </summary>
    /// <param name="text">Text.</param>
    /// <param name="source">Message source.</param>
    public void Error(string text, string source = "Decompiler") => ErrorLog(text, source);
}
