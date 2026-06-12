namespace WslPortProxyGuardian;

public interface ILogSink
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

public sealed class CompositeLogSink(params ILogSink[] sinks) : ILogSink
{
    public void Info(string message) => Write(sink => sink.Info(message));
    public void Warn(string message) => Write(sink => sink.Warn(message));
    public void Error(string message) => Write(sink => sink.Error(message));

    private void Write(Action<ILogSink> write)
    {
        foreach (var sink in sinks)
        {
            try
            {
                write(sink);
            }
            catch
            {
                // Logging must never make the guardian less reliable.
            }
        }
    }
}

public sealed class ConsoleLogSink : ILogSink
{
    public void Info(string message) => Write(LogFormatter.Format("INFO", message));
    public void Warn(string message) => Write(LogFormatter.Format("WARN", message));
    public void Error(string message) => Write(LogFormatter.Format("ERROR", message));

    private static void Write(string line)
    {
        Console.WriteLine(line);
    }
}

public sealed class FileLogSink(string path) : ILogSink
{
    private readonly object _gate = new();

    public string Path { get; } = path;

    public void Info(string message) => Write(LogFormatter.Format("INFO", message));
    public void Warn(string message) => Write(LogFormatter.Format("WARN", message));
    public void Error(string message) => Write(LogFormatter.Format("ERROR", message));

    private void Write(string line)
    {
        lock (_gate)
        {
            File.AppendAllText(Path, line + Environment.NewLine);
        }
    }
}

public static class LogSinkFactory
{
    public static ILogSink CreateDefault(string? requestedLogFilePath, out string? logFilePath)
    {
        var console = new ConsoleLogSink();
        try
        {
            logFilePath = string.IsNullOrWhiteSpace(requestedLogFilePath)
                ? LogFilePaths.DefaultForRun()
                : requestedLogFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);
            var file = new FileLogSink(logFilePath);
            return new CompositeLogSink(console, file);
        }
        catch (Exception ex)
        {
            logFilePath = null;
            console.Warn($"File logging is unavailable: {ex.Message}");
            return console;
        }
    }
}

public static class LogFilePaths
{
    public static string DefaultForRun()
    {
        return Path.Combine(
            Environment.CurrentDirectory,
            "logs",
            $"wslportproxy-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}-p{Environment.ProcessId}.log");
    }
}

public static class LogFormatter
{
    public static string Format(string level, string message)
    {
        return $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {level} [pid {Environment.ProcessId}] {message}";
    }
}
