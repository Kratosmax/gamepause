using System.Diagnostics;
using GamePause.Core;

var arguments = args
    .Select((value, index) => (value, index))
    .Where(item => item.value.StartsWith("--", StringComparison.Ordinal))
    .ToDictionary(
        item => item.value,
        item => item.index + 1 < args.Length ? args[item.index + 1] : string.Empty,
        StringComparer.OrdinalIgnoreCase);

if (!arguments.TryGetValue("--owner", out var ownerValue)
    || !int.TryParse(ownerValue, out var ownerProcessId)
    || !arguments.TryGetValue("--data", out var dataDirectory)
    || string.IsNullOrWhiteSpace(dataDirectory))
{
    return 2;
}

var safetyPolicy = new SafetyPolicy();
var store = new SessionStore(dataDirectory);
var service = new ProcessSuspensionService(
    new ProcessCatalog(safetyPolicy),
    new NativeProcessApi(),
    safetyPolicy,
    store);
try
{
    using var owner = Process.GetProcessById(ownerProcessId);
    var failureTracker = new ShellFailureTracker();
    while (!owner.WaitForExit(500))
    {
        var session = store.Load();
        var states = session?.Targets.SelectMany(target => target.Processes).ToArray() ?? [];
        var pauseComplete = states.Any(state => state.State == SuspensionState.Suspended)
                            && states.All(state => state.State is not (SuspensionState.Planned or SuspensionState.Suspending));
        var responsive = !pauseComplete || ShellHealthProbe.IsTaskbarResponsive(750);
        var nextFailureCount = responsive ? 0 : failureTracker.ConsecutiveFailures + 1;
        if (!responsive)
        {
            store.Log($"Watchdog taskbar responsiveness probe failed ({nextFailureCount}/2).");
        }
        if (failureTracker.Observe(pauseComplete, responsive))
        {
            var safetyResult = service.ResumeActiveSessionIfStable();
            store.Log($"Watchdog shell-safety recovery result: {safetyResult.Message}");
        }
    }
}
catch (ArgumentException)
{
    // The owner already exited; recovery should run immediately.
}

Thread.Sleep(400);
var result = service.ResumeActiveSession();
store.Log($"Watchdog recovery result: {result.Message}");
return result.Success ? 0 : 1;
