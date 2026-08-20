using System.Runtime.InteropServices;

namespace GamePause.Core;

internal static class NativeMethods
{
    internal const uint Th32csSnapProcess = 0x00000002;
    internal const uint ProcessSuspendResume = 0x0800;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint ProcessQueryInformation = 0x0400;
    internal const uint ProcessSetQuota = 0x0100;
    internal const uint ThreadQueryLimitedInformation = 0x0800;
    internal const int ThreadSuspendCount = 35;
    internal static readonly nint InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct FileTime
    {
        internal uint LowDateTime;
        internal uint HighDateTime;

        internal long ToInt64() => (long)(((ulong)HighDateTime << 32) | LowDateTime);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ProcessEntry32
    {
        internal uint dwSize;
        internal uint cntUsage;
        internal uint th32ProcessID;
        internal nint th32DefaultHeapID;
        internal uint th32ModuleID;
        internal uint cntThreads;
        internal uint th32ParentProcessID;
        internal int pcPriClassBase;
        internal uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string szExeFile;
    }

    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Process32FirstW(nint hSnapshot, ref ProcessEntry32 lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Process32NextW(nint hSnapshot, ref ProcessEntry32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessTimes(
        nint hProcess,
        out FileTime lpCreationTime,
        out FileTime lpExitTime,
        out FileTime lpKernelTime,
        out FileTime lpUserTime);

    [DllImport("ntdll.dll")]
    internal static extern int NtSuspendProcess(nint processHandle);

    [DllImport("ntdll.dll")]
    internal static extern int NtResumeProcess(nint processHandle);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EmptyWorkingSet(nint processHandle);

    [DllImport("ntdll.dll")]
    internal static extern int NtQueryInformationThread(
        nint threadHandle,
        int threadInformationClass,
        out uint threadInformation,
        uint threadInformationLength,
        out uint returnLength);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLengthW(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextW(nint hWnd, char[] lpString, int nMaxCount);
}
