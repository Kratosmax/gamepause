using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace GamePause.App;

internal enum ElevationLaunchResult
{
    Started,
    Cancelled,
    Failed
}

internal static class ElevationService
{
    internal static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static ElevationLaunchResult RestartAsAdministrator(IEnumerable<string> arguments, out string? error)
    {
        error = null;
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            error = "无法确定 Game Pause 程序路径。";
            return ElevationLaunchResult.Failed;
        }

        try
        {
            var startInfo = CreateRestartStartInfo(executablePath, arguments);
            return Process.Start(startInfo) is null
                ? ElevationLaunchResult.Failed
                : ElevationLaunchResult.Started;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return ElevationLaunchResult.Cancelled;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            error = exception.Message;
            return ElevationLaunchResult.Failed;
        }
    }

    internal static ProcessStartInfo CreateRestartStartInfo(string executablePath, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true,
            Verb = "runas"
        };
        foreach (var argument in arguments.Where(argument =>
                     !string.Equals(argument, "--elevation-attempted", StringComparison.OrdinalIgnoreCase)))
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.ArgumentList.Add("--elevation-attempted");
        return startInfo;
    }
}
