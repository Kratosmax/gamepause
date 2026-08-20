using System.Runtime.InteropServices;

namespace GamePause.App;

internal static class GlobalHotkeys
{
    internal const int ToggleId = 0x4101;
    internal const int EmergencyResumeId = 0x4102;
    internal const int WmHotkey = 0x0312;
    internal const uint Alt = 0x0001;
    internal const uint Control = 0x0002;
    internal const uint Shift = 0x0004;
    internal const uint NoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint hWnd, int id);
}
