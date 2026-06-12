using WslPortProxyGuardian;

namespace WslPortProxyGuardian.Tests;

public sealed class LoggerTests
{
    [Fact]
    public void FileLogSinkWritesMessages()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wslportproxy-test-{Guid.NewGuid():N}.log");
        try
        {
            var sink = new FileLogSink(path);

            sink.Info("hello file log");

            var text = File.ReadAllText(path);
            Assert.Contains("INFO", text, StringComparison.Ordinal);
            Assert.Contains("hello file log", text, StringComparison.Ordinal);
            Assert.Contains("pid", text, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void CompositeLogSinkContinuesWhenOneSinkFails()
    {
        var capture = new CaptureLogSink();
        var sink = new CompositeLogSink(new ThrowingLogSink(), capture);

        sink.Error("keep logging");

        Assert.Contains("keep logging", capture.Messages);
    }

    [Fact]
    public void DefaultLogPathUsesCurrentDirectory()
    {
        var path = LogFilePaths.DefaultForRun();
        var expectedDirectory = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "logs"));
        var actualDirectory = Path.GetFullPath(Path.GetDirectoryName(path)!);

        Assert.Equal(expectedDirectory, actualDirectory);
        Assert.StartsWith("wslportproxy-", Path.GetFileName(path), StringComparison.Ordinal);
        Assert.EndsWith($"-p{Environment.ProcessId}.log", Path.GetFileName(path), StringComparison.Ordinal);
    }

    [Fact]
    public void FormatterIncludesMilliseconds()
    {
        var line = LogFormatter.Format("INFO", "hello");

        Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] INFO \[pid \d+\] hello$", line);
    }

    private sealed class CaptureLogSink : ILogSink
    {
        public List<string> Messages { get; } = [];
        public void Info(string message) => Messages.Add(message);
        public void Warn(string message) => Messages.Add(message);
        public void Error(string message) => Messages.Add(message);
    }

    private sealed class ThrowingLogSink : ILogSink
    {
        public void Info(string message) => throw new IOException("nope");
        public void Warn(string message) => throw new IOException("nope");
        public void Error(string message) => throw new IOException("nope");
    }
}
