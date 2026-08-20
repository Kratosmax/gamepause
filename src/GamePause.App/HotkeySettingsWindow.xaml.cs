using System.Collections.ObjectModel;
using System.Windows.Controls;
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
    private readonly Func<System.Windows.Window, UpdateNetworkSettings, Task>? _checkForUpdates;
    private readonly ObservableCollection<GithubProxyEditorRow> _githubProxies = [];

    internal HotkeySettingsWindow(
        HotkeySettings current,
        bool startupEnabled,
        string currentVersion,
        UpdateNetworkSettings currentNetworkSettings,
        Func<System.Windows.Window, UpdateNetworkSettings, Task>? checkForUpdates = null)
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

        var normalizedNetworkSettings = currentNetworkSettings.Normalize();
        foreach (var proxy in normalizedNetworkSettings.GithubProxies ?? [])
            _githubProxies.Add(new GithubProxyEditorRow(proxy));
        GithubProxyGrid.ItemsSource = _githubProxies;
        HttpProxyBox.Text = normalizedNetworkSettings.HttpProxy ?? string.Empty;

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
    internal UpdateNetworkSettings SelectedUpdateNetworkSettings { get; private set; } = UpdateNetworkSettings.Default;

    internal void ShowNetworkSettingsForVisualQa()
    {
        SettingsTabs.SelectedIndex = 1;
        GithubProxyGrid.SelectedIndex = 1;
    }

    private void ToggleBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) => CaptureGesture(e, true);
    private void EmergencyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e) => CaptureGesture(e, false);

    private async void CheckUpdate_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_checkForUpdates is null || !TryBuildNetworkSettings(out var settings)) return;
        CheckUpdateButton.IsEnabled = false;
        try
        {
            await _checkForUpdates(this, settings);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private void AddProxy_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var row = new GithubProxyEditorRow(new GithubProxySetting("https://", 5));
        _githubProxies.Add(row);
        GithubProxyGrid.SelectedItem = row;
        GithubProxyGrid.ScrollIntoView(row);
    }

    private void RemoveProxy_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (GithubProxyGrid.SelectedItem is not GithubProxyEditorRow row) return;
        if (row.IsDirect)
        {
            WpfMessageBox.Show("GitHub 直连是永久线路，不能删除；可将其优先级设为 0 来禁用。", "网络设置");
            return;
        }
        _githubProxies.Remove(row);
    }

    private void GithubProxyGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.Item is GithubProxyEditorRow { IsDirect: true } && e.Column.DisplayIndex == 0)
            e.Cancel = true;
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
        if (!TryBuildNetworkSettings(out var networkSettings)) return;

        SelectedSettings = new HotkeySettings(_toggleGesture, _emergencyGesture);
        StartupEnabled = StartupCheck.IsChecked == true;
        SelectedUpdateNetworkSettings = networkSettings;
        DialogResult = true;
    }

    private bool TryBuildNetworkSettings(out UpdateNetworkSettings settings)
    {
        GithubProxyGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        GithubProxyGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var proxies = new List<GithubProxySetting>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _githubProxies)
        {
            if (row.IsDirect)
            {
                proxies.Add(new GithubProxySetting(string.Empty, row.Priority, true));
                continue;
            }
            if (!UpdateNetworkSettings.TryNormalizeGithubProxy(row.Address, out var baseUrl))
            {
                WpfMessageBox.Show($"GitHub 加速地址无效：{row.Address}\n\n请输入完整的 http:// 或 https:// 地址，且不要包含查询参数。", "网络设置");
                settings = UpdateNetworkSettings.Default;
                return false;
            }
            if (!seen.Add(baseUrl))
            {
                WpfMessageBox.Show($"GitHub 加速地址重复：{baseUrl}", "网络设置");
                settings = UpdateNetworkSettings.Default;
                return false;
            }
            proxies.Add(new GithubProxySetting(baseUrl, row.Priority));
        }

        if (!UpdateNetworkSettings.TryNormalizeHttpProxy(HttpProxyBox.Text, out var httpProxy))
        {
            WpfMessageBox.Show("HTTP 网络代理无效。请输入类似 http://127.0.0.1:7890 的地址；暂不支持账号密码。", "网络设置");
            settings = UpdateNetworkSettings.Default;
            return false;
        }
        if (proxies.All(item => item.Priority == 0))
        {
            WpfMessageBox.Show("至少启用一条 GitHub 访问线路，优先级需设为 1 到 10。", "网络设置");
            settings = UpdateNetworkSettings.Default;
            return false;
        }

        settings = new UpdateNetworkSettings(proxies, httpProxy).Normalize();
        return true;
    }

    private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e) => DialogResult = false;
}

internal sealed class GithubProxyEditorRow
{
    internal GithubProxyEditorRow(GithubProxySetting setting)
    {
        IsDirect = setting.IsDirect;
        Address = setting.IsDirect ? "GitHub 直连（不拼接加速地址）" : setting.BaseUrl;
        Priority = setting.Priority;
    }

    public string Address { get; set; }
    public int Priority { get; set; }
    public bool IsDirect { get; }
}
