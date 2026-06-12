using WslPortProxyGuardian;

var startupArguments = StartupArguments.Parse(args);
var options = CliParser.Parse([.. startupArguments.PublicArgs]);
if (options.ShowHelp)
{
    if (!string.IsNullOrWhiteSpace(options.Error))
    {
        Console.Error.WriteLine(options.Error);
        Console.Error.WriteLine();
    }

    Console.WriteLine(HelpText.Render(options.ShowRunHelp));
    return string.IsNullOrWhiteSpace(options.Error) ? 0 : 1;
}

var logSink = LogSinkFactory.CreateDefault(startupArguments.LogFilePath, out var logFilePath);
if (!string.IsNullOrWhiteSpace(logFilePath))
{
    logSink.Info($"Log file: {logFilePath}");
}

var administratorElevation = new WindowsAdministratorElevation();
if (!administratorElevation.IsAdministrator())
{
    return administratorElevation.TryRelaunchElevated(startupArguments.PublicArgs, logFilePath, logSink) ? 0 : 1;
}

var processRunner = new ProcessRunner();
var tcpStateProvider = new SystemTcpStateProvider();
var service = new GuardianService(
    new WslAddressResolver(processRunner),
    new NetshPortProxyManager(processRunner),
    new NetshFirewallRuleManager(processRunner),
    new PortConflictDetector(processRunner, tcpStateProvider),
    logSink,
    new WindowsPrivilegeChecker(),
    new TcpConnectionMonitor(tcpStateProvider, logSink),
    new WslForwardingDiagnostics(processRunner, logSink));

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    logSink.Warn("Shutdown requested. Cleaning up managed mappings.");
    cts.Cancel();
};

AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    service.CleanupAsync(options).GetAwaiter().GetResult();
};

try
{
    var exitCode = await service.RunAsync(options, cts.Token);
    await service.CleanupAsync(options);
    return exitCode;
}
catch (OperationCanceledException)
{
    await service.CleanupAsync(options);
    return 0;
}
catch (Exception ex)
{
    logSink.Error(ex.Message);
    await service.CleanupAsync(options);
    return 1;
}
