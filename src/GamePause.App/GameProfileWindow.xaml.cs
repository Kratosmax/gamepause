using GamePause.Core;

namespace GamePause.App;

public partial class GameProfileWindow : System.Windows.Window
{
    private readonly Guid _id;
    private readonly string _processName;
    private readonly string? _executablePath;

    internal GameProfileWindow(GameProfile profile)
    {
        InitializeComponent();
        _id = profile.Id;
        _processName = profile.ProcessName;
        _executablePath = profile.ExecutablePath;
        DisplayNameBox.Text = profile.DisplayName;
        ProcessNameBox.Text = profile.ProcessName;
        PathBox.Text = profile.ExecutablePath ?? "路径不可读取，将按进程名匹配";
        DeepPauseCheck.IsChecked = profile.PauseMode == GamePauseMode.Deep;
        AutoPauseCheck.IsChecked = profile.AutoPauseEnabled;
        DelayBox.Text = profile.FocusLossDelaySeconds.ToString();
        AutoResumeCheck.IsChecked = profile.AutoResumeEnabled;
        AllowCautionCheck.IsChecked = profile.AllowCautionAutomaticRules;
        UpdateAutomaticControls();
    }

    internal GameProfile? SelectedProfile { get; private set; }

    private void AutoPauseCheck_Changed(object sender, System.Windows.RoutedEventArgs e) => UpdateAutomaticControls();

    private void UpdateAutomaticControls()
    {
        if (!IsInitialized) return;
        var enabled = AutoPauseCheck.IsChecked == true;
        DelayBox.IsEnabled = enabled;
        AutoResumeCheck.IsEnabled = enabled;
        AllowCautionCheck.IsEnabled = enabled;
    }

    private void SaveButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var displayName = DisplayNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            System.Windows.MessageBox.Show("请输入档案显示名称。", "游戏档案", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(DelayBox.Text.Trim(), out var delay) || delay is < 3 or > 300)
        {
            System.Windows.MessageBox.Show("自动暂停延迟必须是 3 到 300 秒之间的整数。", "游戏档案",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        SelectedProfile = new GameProfile(
            _id, displayName, _processName, _executablePath,
            DeepPauseCheck.IsChecked == true ? GamePauseMode.Deep : GamePauseMode.Standard,
            AutoPauseCheck.IsChecked == true, delay,
            AutoPauseCheck.IsChecked == true && AutoResumeCheck.IsChecked == true,
            AutoPauseCheck.IsChecked == true && AllowCautionCheck.IsChecked == true);
        DialogResult = true;
    }
}
