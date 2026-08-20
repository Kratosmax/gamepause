using System.Runtime.InteropServices;

internal static class ShellHealthProbe
{
    private const uint WmNull = 0x0000;
    private const uint SmtoBlock = 0x0001;
    private const uint SmtoAbortIfHung = 0x0002;

    internal static bool IsTaskbarResponsive(uint timeoutMilliseconds)
    {
        var taskbar = FindWindowW("Shell_TrayWnd", null);
        return taskbar != nint.Zero
               && SendMessageTimeoutW(
                   taskbar,
                   WmNull,
                   nint.Zero,
                   nint.Zero,
                   SmtoBlock | SmtoAbortIfHung,
                   timeoutMilliseconds,
                   out _) != nint.Zero;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowW(string? className, string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SendMessageTimeoutW(
        nint window,
        uint message,
        nint wParam,
        nint lParam,
        uint flags,
        uint timeout,
        out nint result);
}
