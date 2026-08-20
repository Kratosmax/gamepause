using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using WpfMessageBox = System.Windows.MessageBox;

namespace GamePause.App;

public partial class HotkeySettingsWindow : System.Windows.Window
{
    private HotkeyGesture? _toggleGesture;
    private HotkeyGesture? _emergencyGesture;
    private readonly Func<System.Windows.Window, Task>? _checkForUpdates;

    internal HotkeySettingsWindow(
        HotkeySettings current,
        bool startupEnabled,
        string currentVersion,
        Func<System.Windows.Window, Task>? checkForUpdates = null)
    {
        InitializeComponent();
        _checkForUpdates = checkForUpdates;
        _toggleGesture = current.Toggle;
        _emergencyGesture = current.Emergency;
        ToggleBox.Text = current.Toggle.DisplayText;
        EmergencyBox.Text = current.Emergency.DisplayText;
        StartupCheck.IsChecked = startupEnabled;
        CurrentVersionText.Text = $"当前版本 {currentVersion}";
        CheckUpdateButton.IsEnabled = checkForUpdates is not null;
        SourceInitialized += (_, _) =>
        {
            if (!WpfBackdrop.TryApply(this))
            {
                Background = new SolidColorBrush(MediaColor.FromRgb(239, 244, 248));
                RootSurface.Background = new SolidColorBrush(MediaColor.FromRgb(239, 244, 248));
            }
        };
    }

    internal HotkeySettings SelectedSettings { get; private set; } = HotkeySettings.Default;
    internal bool StartupEnabled { get; private set; }

    private void ToggleBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) => CaptureGesture(e, true);
    private void EmergencyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) => CaptureGesture(e, false);

    private async void CheckUpdate_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_checkForUpdates is null) return;
        CheckUpdateButton.IsEnabled = false;
        try
        {
            await _checkForUpdates(this);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void CaptureGesture(System.Windows.Input.KeyEventArgs e, bool toggle)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt) return;

        uint modifiers = 0;
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) modifiers |= GlobalHotkeys.Control;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) modifiers |= GlobalHotkeys.Alt;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) modifiers |= GlobalHotkeys.Shift;
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (modifiers == 0 || virtualKey == 0)
        {
            System.Media.SystemSounds.Beep.Play();
            return;
        }

        var gesture = new HotkeyGesture(modifiers, (Forms.Keys)virtualKey);
        if (toggle)
        {
            _toggleGesture = gesture;
            ToggleBox.Text = gesture.DisplayText;
        }
        else
        {
            _emergencyGesture = gesture;
            EmergencyBox.Text = gesture.DisplayText;
        }
    }

    private void Save_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_toggleGesture is null || _emergencyGesture is null)
        {
            WpfMessageBox.Show("两组快捷键都必须包含修饰键和一个普通按键。", "设置");
            return;
        }
        if (_toggleGesture == _emergencyGesture)
        {
            WpfMessageBox.Show("两组快捷键不能相同。", "设置");
            return;
        }
        SelectedSettings = new HotkeySettings(_toggleGesture, _emergencyGesture);
        StartupEnabled = StartupCheck.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e) => DialogResult = false;
}
