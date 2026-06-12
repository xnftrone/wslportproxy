using WslPortProxyGuardian;

namespace WslPortProxyGuardian.Tests;

public sealed class PrivilegeCheckerTests
{
    [Fact]
    public void CreatesElevationRequestForPublishedExecutable()
    {
        var request = WindowsAdministratorElevation.CreateLaunchRequest(
            ["run", "-p", "8443:443", "--distro", "Ubuntu 22", StartupArguments.LogFileOption, @"D:\tools\wslportproxy\build\logs\run.log"],
            @"D:\tools\wslportproxy\build\wslportproxy.exe",
            entryCommandPath: null,
            @"D:\tools\wslportproxy");

        Assert.Equal(@"D:\tools\wslportproxy\build\wslportproxy.exe", request.FileName);
        Assert.Equal(@"D:\tools\wslportproxy", request.WorkingDirectory);
        Assert.Equal(@"run -p 8443:443 --distro ""Ubuntu 22"" --_wslportproxy-log-file D:\tools\wslportproxy\build\logs\run.log", request.Arguments);
    }

    [Fact]
    public void CreatesElevationRequestForDotnetHostLaunch()
    {
        var request = WindowsAdministratorElevation.CreateLaunchRequest(
            ["run", "-p", "4444"],
            @"C:\Program Files\dotnet\dotnet.exe",
            @"D:\tools\wsl portproxy\wslportproxy.dll",
            @"D:\tools\wslportproxy");

        Assert.Equal(@"C:\Program Files\dotnet\dotnet.exe", request.FileName);
        Assert.Equal(@"""D:\tools\wsl portproxy\wslportproxy.dll"" run -p 4444", request.Arguments);
    }

    [Fact]
    public void RequiresExecutablePathForElevationRequest()
    {
        Assert.Throws<InvalidOperationException>(() => WindowsAdministratorElevation.CreateLaunchRequest(
            ["run"],
            processPath: null,
            entryCommandPath: null,
            @"D:\tools\wslportproxy"));
    }

    [Fact]
    public void StartupArgumentsExtractsInternalLogFileOption()
    {
        var startupArguments = StartupArguments.Parse([
            "run",
            "-p",
            "9999",
            StartupArguments.LogFileOption,
            @"D:\tools\wslportproxy\logs\run.log"
        ]);

        Assert.Equal(["run", "-p", "9999"], startupArguments.PublicArgs);
        Assert.Equal(@"D:\tools\wslportproxy\logs\run.log", startupArguments.LogFilePath);
    }

    [Fact]
    public void StartupArgumentsRequiresInternalLogFileValue()
    {
        Assert.Throws<ArgumentException>(() => StartupArguments.Parse([StartupArguments.LogFileOption]));
    }
}
