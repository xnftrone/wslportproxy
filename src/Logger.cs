namespace WslPortProxyGuardian;

public interface ILogSink
{
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}

public sealed class ConsoleLogSink : ILogSink
{
    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        Console.WriteLine($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {level} {message}");
    }
}
