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

try
{
    using var owner = Process.GetProcessById(ownerProcessId);
    owner.WaitForExit();
}
catch (ArgumentException)
{
    // The owner already exited; recovery should run immediately.
}

Thread.Sleep(400);
var safetyPolicy = new SafetyPolicy();
var store = new SessionStore(dataDirectory);
var service = new ProcessSuspensionService(
    new ProcessCatalog(safetyPolicy),
    new NativeProcessApi(),
    safetyPolicy,
    store);
var result = service.ResumeActiveSession();
store.Log($"Watchdog recovery result: {result.Message}");
return result.Success ? 0 : 1;
