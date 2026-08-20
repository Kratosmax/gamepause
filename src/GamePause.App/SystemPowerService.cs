using System.Diagnostics;

namespace GamePause.App;

internal static class SystemPowerService
{
    internal static (bool Success, string Message) EnableFullHibernate()
    {
        var enable = RunPowerCfg("/hibernate", "on");
        if (!enable.Success)
        {
            return enable;
        }

        return RunPowerCfg("/hibernate", "/type", "full");
    }

    private static (bool Success, string Message) RunPowerCfg(params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (false, "无法启动 Windows 电源配置工具。");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            var message = string.Join(" ", new[] { output.Trim(), error.Trim() }.Where(value => value.Length > 0));
            return process.ExitCode == 0
                ? (true, message)
                : (false, string.IsNullOrWhiteSpace(message) ? $"powercfg 退出码 {process.ExitCode}。" : message);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return (false, exception.Message);
        }
    }
}
