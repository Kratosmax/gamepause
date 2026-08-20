using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GamePause.Core;

public interface IProcessCatalog
{
    ProcessIdentity? GetForegroundProcess();
    IReadOnlyList<ProcessIdentity> GetProcessTree(int rootProcessId);
    IReadOnlyList<WindowProcessInfo> GetWindowProcesses();
    IReadOnlyList<string> GetRunningProcessNames();
    bool IsSameProcess(ProcessIdentity identity);
}

public sealed class ProcessCatalog : IProcessCatalog
{
    private readonly SafetyPolicy _safetyPolicy;

    public ProcessCatalog(SafetyPolicy safetyPolicy)
    {
        _safetyPolicy = safetyPolicy;
    }

    public ProcessIdentity? GetForegroundProcess()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == nint.Zero)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        return processId == 0 ? null : TryCreateIdentity((int)processId, 0, 0, null);
    }

    public IReadOnlyList<ProcessIdentity> GetProcessTree(int rootProcessId)
    {
        var entries = SnapshotProcesses();
        if (!entries.TryGetValue(rootProcessId, out var root))
        {
            return [];
        }

        var children = entries.Values
            .GroupBy(entry => entry.ParentProcessId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var rootIdentity = TryCreateIdentity(root.ProcessId, root.ParentProcessId, 0, root.Name);
        if (rootIdentity is null) return [];

        var result = new List<ProcessIdentity> { rootIdentity };
        var queue = new Queue<ProcessIdentity>();
        queue.Enqueue(rootIdentity);

        while (queue.TryDequeue(out var current))
        {
            if (!children.TryGetValue(current.ProcessId, out var descendants))
            {
                continue;
            }

            foreach (var child in descendants)
            {
                var childIdentity = TryCreateIdentity(
                    child.ProcessId, child.ParentProcessId, current.Depth + 1, child.Name);
                if (childIdentity is null || childIdentity.StartTimeUtcTicks < current.StartTimeUtcTicks)
                {
                    continue;
                }
                result.Add(childIdentity);
                queue.Enqueue(childIdentity);
            }
        }

        return result;
    }

    public IReadOnlyList<WindowProcessInfo> GetWindowProcesses()
    {
        var processes = new Dictionary<int, WindowProcessInfo>();
        NativeMethods.EnumWindows((window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window))
            {
                return true;
            }

            var titleLength = NativeMethods.GetWindowTextLengthW(window);
            if (titleLength <= 0)
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(window, out var processIdValue);
            var processId = (int)processIdValue;
            if (processId == 0 || processes.ContainsKey(processId))
            {
                return true;
            }

            try
            {
                using var process = Process.GetProcessById(processId);
                var buffer = new char[titleLength + 1];
                NativeMethods.GetWindowTextW(window, buffer, buffer.Length);
                var title = new string(buffer).TrimEnd('\0');
                var name = process.ProcessName;
                string? executablePath = null;
                try
                {
                    executablePath = process.MainModule?.FileName;
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
                {
                    // Compatibility checks can fall back to the process name.
                }
                processes[processId] = new WindowProcessInfo(
                    processId,
                    name,
                    title,
                    process.WorkingSet64,
                    _safetyPolicy.IsProtected(processId, name),
                    executablePath);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // Processes can exit while windows are being enumerated.
            }

            return true;
        }, nint.Zero);

        return processes.Values
            .OrderBy(process => process.IsProtected)
            .ThenBy(process => process.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool IsSameProcess(ProcessIdentity identity)
    {
        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            return process.StartTime.ToUniversalTime().Ticks == identity.StartTimeUtcTicks;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    public IReadOnlyList<string> GetRunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { names.Add(process.ProcessName); }
                catch (Exception exception) when (exception is InvalidOperationException or Win32Exception) { }
            }
        }
        return names.ToArray();
    }

    private static ProcessIdentity? TryCreateIdentity(
        int processId,
        int parentProcessId,
        int depth,
        string? snapshotName)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            string? executablePath = null;
            try
            {
                executablePath = process.MainModule?.FileName;
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                // Path access is optional; PID plus start time is the identity boundary.
            }

            return new ProcessIdentity(
                processId,
                parentProcessId,
                depth,
                snapshotName ?? process.ProcessName,
                executablePath,
                process.StartTime.ToUniversalTime().Ticks);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private static Dictionary<int, SnapshotEntry> SnapshotProcesses()
    {
        var snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.Th32csSnapProcess, 0);
        if (snapshot == NativeMethods.InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to enumerate processes.");
        }

        try
        {
            var entries = new Dictionary<int, SnapshotEntry>();
            var entry = new NativeMethods.ProcessEntry32
            {
                dwSize = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry32>(),
                szExeFile = string.Empty
            };

            if (!NativeMethods.Process32FirstW(snapshot, ref entry))
            {
                return entries;
            }

            do
            {
                var processId = (int)entry.th32ProcessID;
                entries[processId] = new SnapshotEntry(
                    processId,
                    (int)entry.th32ParentProcessID,
                    entry.szExeFile);
            }
            while (NativeMethods.Process32NextW(snapshot, ref entry));

            return entries;
        }
        finally
        {
            NativeMethods.CloseHandle(snapshot);
        }
    }

    private sealed record SnapshotEntry(int ProcessId, int ParentProcessId, string Name);
}
