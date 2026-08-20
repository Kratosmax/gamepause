using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;

namespace GamePause.App;

internal static class WpfBackdrop
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;

    internal static bool TryApply(System.Windows.Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) ||
            AppContext.TryGetSwitch("GamePause.DisableBackdrop", out var disabled) && disabled)
        {
            return false;
        }

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == nint.Zero) return false;
        if (HwndSource.FromHwnd(handle) is { CompositionTarget: not null } source)
        {
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
        }

        var rounded = 2;
        DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref rounded, sizeof(int));
        var transientBackdrop = 3;
        var backdropResult = DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref transientBackdrop, sizeof(int));
        var fullWindow = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        var frameResult = DwmExtendFrameIntoClientArea(handle, ref fullWindow);
        return backdropResult == 0 && frameResult == 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint window, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        internal int Left;
        internal int Right;
        internal int Top;
        internal int Bottom;
    }
}
