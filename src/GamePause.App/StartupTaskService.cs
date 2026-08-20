using System.Diagnostics;

namespace GamePause.App;

internal sealed record StartupTaskResult(bool Success, string Message);

internal static class StartupTaskService
{
    private const string TaskName = "GamePause.AutoStart";

    internal static bool IsEnabled() => Run(["/Query", "/TN", TaskName]).ExitCode == 0;

    internal static StartupTaskResult SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            var deleted = Run(["/Delete", "/TN", TaskName, "/F"]);
            return deleted.ExitCode == 0 || !IsEnabled()
                ? new StartupTaskResult(true, "已关闭开机静默启动。")
                : new StartupTaskResult(false, CleanMessage(deleted.Output, "无法删除开机启动任务。"));
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return new StartupTaskResult(false, "无法确定 Game Pause 程序路径。");
        }

        var created = Run(BuildCreateArguments(executablePath));
        return created.ExitCode == 0
            ? new StartupTaskResult(true, "已启用开机静默启动。")
            : new StartupTaskResult(false, CleanMessage(created.Output, "无法创建开机启动任务。"));
    }

    internal static IReadOnlyList<string> BuildCreateArguments(string executablePath) =>
    [
        "/Create", "/TN", TaskName,
        "/TR", $"\"{executablePath}\" --silent",
        "/SC", "ONLOGON", "/RL", "LIMITED", "/F"
    ];

    private static (int ExitCode, string Output) Run(IEnumerable<string> arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
            using var process = Process.Start(startInfo);
            if (process is null) return (-1, "无法启动 Windows 任务计划程序。");
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(8000);
            if (process.HasExited) return (process.ExitCode, output);
            try { process.Kill(); } catch (InvalidOperationException) { }
            return (-1, "Windows 任务计划程序响应超时。");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return (-1, exception.Message);
        }
    }

    private static string CleanMessage(string message, string fallback) =>
        string.IsNullOrWhiteSpace(message) ? fallback : message.Trim();
}
