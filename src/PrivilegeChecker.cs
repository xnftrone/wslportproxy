using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace WslPortProxyGuardian;

public interface IPrivilegeChecker
{
    void EnsureAdministrator();
}

public interface IAdministratorElevation
{
    bool IsAdministrator();
    bool TryRelaunchElevated(IReadOnlyList<string> arguments, string? logFilePath, ILogSink logSink);
}

public sealed class WindowsPrivilegeChecker : IPrivilegeChecker
{
    public void EnsureAdministrator()
    {
        if (!WindowsAdministratorElevation.IsCurrentProcessAdministrator())
        {
            throw new InvalidOperationException("Run this tool from an elevated terminal.");
        }
    }
}

public sealed class WindowsAdministratorElevation : IAdministratorElevation
{
    public bool IsAdministrator() => IsCurrentProcessAdministrator();

    public bool TryRelaunchElevated(IReadOnlyList<string> arguments, string? logFilePath, ILogSink logSink)
    {
        try
        {
            var elevatedArguments = new List<string>(arguments);
            if (!string.IsNullOrWhiteSpace(logFilePath))
            {
                elevatedArguments.Add(StartupArguments.LogFileOption);
                elevatedArguments.Add(logFilePath);
            }

            var request = CreateLaunchRequest(
                elevatedArguments,
                Environment.ProcessPath,
                Environment.GetCommandLineArgs().FirstOrDefault(),
                Environment.CurrentDirectory);

            logSink.Warn("Administrator privileges are required. Requesting elevation via UAC.");
            StartElevated(request);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            logSink.Error("Elevation request was cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            logSink.Error($"Failed to request administrator privileges: {ex.Message}");
            return false;
        }
    }

    public static bool IsCurrentProcessAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static ElevationLaunchRequest CreateLaunchRequest(
        IReadOnlyList<string> arguments,
        string? processPath,
        string? entryCommandPath,
        string currentDirectory)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("Unable to determine the current executable path for elevation.");
        }

        var launchArguments = arguments;
        if (IsDotnetHost(processPath) && !string.IsNullOrWhiteSpace(entryCommandPath))
        {
            launchArguments = [entryCommandPath, .. arguments];
        }

        return new ElevationLaunchRequest(
            processPath,
            ProcessArgumentBuilder.Join([.. launchArguments]),
            currentDirectory);
    }

    private static bool IsDotnetHost(string processPath)
    {
        var fileName = Path.GetFileName(processPath);
        return string.Equals(fileName, "dotnet.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, "dotnet", StringComparison.OrdinalIgnoreCase);
    }

    private static void StartElevated(ElevationLaunchRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = true,
            Verb = "runas"
        };

        var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("Failed to start elevated process.");
        }
    }
}

public sealed record ElevationLaunchRequest(string FileName, string Arguments, string WorkingDirectory);
