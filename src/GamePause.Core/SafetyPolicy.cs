namespace GamePause.Core;

public sealed class SafetyPolicy
{
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "idle", "system", "secure system", "registry", "memory compression",
        "smss", "csrss", "wininit", "services", "lsass", "winlogon",
        "fontdrvhost", "dwm", "sihost", "svchost", "conhost", "audiodg",
        "explorer", "taskhostw", "runtimebroker", "searchhost",
        "startmenuexperiencehost", "shellexperiencehost", "securityhealthservice",
        "dynamicdependencylifetimemanagershadow", "douyin_tray",
        "gamepause", "gamepause.watchdog"
    };

    public bool IsProtected(ProcessIdentity process)
        => IsProtected(process.ProcessId, process.Name);

    public bool IsProtected(int processId, string processName)
    {
        if (processId <= 4 || processId == Environment.ProcessId)
        {
            return true;
        }

        var normalized = Path.GetFileNameWithoutExtension(processName);
        return string.IsNullOrWhiteSpace(normalized) || ProtectedNames.Contains(normalized);
    }
}
