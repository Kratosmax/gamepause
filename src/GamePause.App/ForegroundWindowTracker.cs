using System.Runtime.InteropServices;

namespace GamePause.App;

internal sealed class ForegroundWindowTracker : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint WineventOutOfContext = 0x0000;
    private readonly WinEventDelegate _callback;
    private nint _hook;
    private int _lastExternalProcessId;

    internal ForegroundWindowTracker()
    {
        _callback = OnForegroundChanged;
        _hook = SetWinEventHook(
            EventSystemForeground,
            EventSystemForeground,
            nint.Zero,
            _callback,
            0,
            0,
            WineventOutOfContext);
        CaptureCurrentForeground();
    }

    internal int? RelevantProcessId
    {
        get
        {
            CaptureCurrentForeground();
            var processId = Volatile.Read(ref _lastExternalProcessId);
            return processId > 0 ? processId : null;
        }
    }

    internal int? CurrentProcessId
    {
        get
        {
            var window = GetForegroundWindow();
            if (window == nint.Zero) return null;
            GetWindowThreadProcessId(window, out var processId);
            return processId > 0 ? (int)processId : null;
        }
    }

    public void Dispose()
    {
        var hook = Interlocked.Exchange(ref _hook, nint.Zero);
        if (hook != nint.Zero)
        {
            UnhookWinEvent(hook);
        }
    }

    private void CaptureCurrentForeground()
    {
        var window = GetForegroundWindow();
        if (window != nint.Zero)
        {
            RecordWindowProcess(window);
        }
    }

    private void OnForegroundChanged(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime)
    {
        if (window != nint.Zero)
        {
            RecordWindowProcess(window);
        }
    }

    private void RecordWindowProcess(nint window)
    {
        GetWindowThreadProcessId(window, out var processId);
        if (processId > 0 && processId != Environment.ProcessId)
        {
            Volatile.Write(ref _lastExternalProcessId, (int)processId);
        }
    }

    private delegate void WinEventDelegate(
        nint hook,
        uint eventType,
        nint window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll")]
    private static extern nint SetWinEventHook(
        uint eventMin,
        uint eventMax,
        nint eventHookAssembly,
        WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
