using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GamePause.Core;

public interface IProcessNativeApi
{
    void Suspend(ProcessIdentity identity);
    void Resume(ProcessIdentity identity);
    bool IsSuspended(int processId);
    WorkingSetTrimResult TrimWorkingSet(int processId);
}

public sealed class NativeProcessApi : IProcessNativeApi
{
    public void Suspend(ProcessIdentity identity)
        => Execute(identity, NativeMethods.NtSuspendProcess, "suspend");

    public void Resume(ProcessIdentity identity)
        => Execute(identity, NativeMethods.NtResumeProcess, "resume");

    public bool IsSuspended(int processId)
    {
        using var process = Process.GetProcessById(processId);
        var inspectedThreads = 0;
        foreach (ProcessThread thread in process.Threads)
        {
            var handle = NativeMethods.OpenThread(
                NativeMethods.ThreadQueryLimitedInformation,
                false,
                (uint)thread.Id);
            if (handle == nint.Zero)
            {
                continue;
            }

            try
            {
                var status = NativeMethods.NtQueryInformationThread(
                    handle,
                    NativeMethods.ThreadSuspendCount,
                    out var suspendCount,
                    sizeof(uint),
                    out _);
                if (status != 0)
                {
                    continue;
                }

                inspectedThreads++;
                if (suspendCount == 0)
                {
                    return false;
                }
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }

        return inspectedThreads > 0;
    }

    public WorkingSetTrimResult TrimWorkingSet(int processId)
    {
        using var process = Process.GetProcessById(processId);
        process.Refresh();
        var beforeBytes = process.WorkingSet64;
        var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessQueryInformation | NativeMethods.ProcessSetQuota,
            false,
            (uint)processId);
        if (handle == nint.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Unable to open process {processId} to trim its working set.");
        }

        try
        {
            if (!NativeMethods.EmptyWorkingSet(handle))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"Windows could not trim the working set for process {processId}.");
            }
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }

        process.Refresh();
        return new WorkingSetTrimResult(beforeBytes, process.WorkingSet64);
    }

    private static void Execute(ProcessIdentity identity, Func<nint, int> operation, string operationName)
    {
        var handle = NativeMethods.OpenProcess(
            NativeMethods.ProcessSuspendResume | NativeMethods.ProcessQueryLimitedInformation,
            false,
            (uint)identity.ProcessId);

        if (handle == nint.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Unable to open process {identity.ProcessId} to {operationName} it.");
        }

        try
        {
            if (!NativeMethods.GetProcessTimes(handle, out var creationTime, out _, out _, out _))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    $"Unable to verify process {identity.ProcessId} before trying to {operationName} it.");
            }

            var actualStartTimeUtcTicks = DateTime.FromFileTimeUtc(creationTime.ToInt64()).Ticks;
            if (actualStartTimeUtcTicks != identity.StartTimeUtcTicks)
            {
                throw new ProcessIdentityMismatchException(
                    $"Process {identity.ProcessId} exited or its PID was reused before the {operationName} operation.");
            }

            var status = operation(handle);
            if (status != 0)
            {
                throw new InvalidOperationException(
                    $"Windows returned NTSTATUS 0x{status:X8} while trying to {operationName} process {identity.ProcessId}.");
            }
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}
