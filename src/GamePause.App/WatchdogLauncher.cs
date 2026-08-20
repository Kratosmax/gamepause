using System.Diagnostics;
using GamePause.Core;

namespace GamePause.App;

internal static class WatchdogLauncher
{
    internal static bool Start(SessionStore store)
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "GamePause.Watchdog.exe");
        if (!File.Exists(executable))
        {
            store.Log("Watchdog executable was not found next to the application.");
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--owner");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add("--data");
            startInfo.ArgumentList.Add(store.DataDirectory);
            Process.Start(startInfo);
            return true;
        }
        catch (Exception exception)
        {
            store.Log($"Unable to start watchdog: {exception.Message}");
            return false;
        }
    }
}
