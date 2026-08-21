using System.ComponentModel;

namespace GamePause.App;

internal enum UpdatePromptSelection { Later, Update, Skip }

public partial class UpdatePromptWindow : System.Windows.Window
{
    private readonly Func<IProgress<UpdateDownloadProgress>, CancellationToken, Task<bool>>? _install;
    private readonly Action<Exception>? _reportError;
    private CancellationTokenSource? _downloadCancellation;
    private bool _downloadInProgress;

    internal UpdatePromptWindow(
        string currentVersion,
        string newVersion,
        string? releaseNotes,
        Func<IProgress<UpdateDownloadProgress>, CancellationToken, Task<bool>>? install = null,
        Action<Exception>? reportError = null)
    {
        InitializeComponent();
        _install = install;
        _reportError = reportError;
        VersionText.Text = $"Game Pause {newVersion}  ·  当前版本 {currentVersion}";
        NotesText.Text = string.IsNullOrWhiteSpace(releaseNotes) ? "此版本没有提供更新说明。" : releaseNotes.Trim();
    }

    internal UpdatePromptSelection Selection { get; private set; } = UpdatePromptSelection.Later;
    internal bool InstallLaunched { get; private set; }

    private async void Update_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_downloadInProgress)
        {
            InstallButton.IsEnabled = false;
            StatusText.Text = "正在取消下载...";
            _downloadCancellation?.Cancel();
            return;
        }

        Selection = UpdatePromptSelection.Update;
        if (_install is null)
        {
            DialogResult = true;
            return;
        }

        SetDownloading(true);
        _downloadCancellation = new CancellationTokenSource();
        var progress = new WindowProgress(this);
        try
        {
            var launched = await _install(progress, _downloadCancellation.Token);
            if (launched)
            {
                InstallLaunched = true;
                _downloadInProgress = false;
                DialogResult = true;
            }
            else
            {
                StatusText.Text = "本次更新已取消，当前版本未被修改。";
                SetDownloading(false);
            }
        }
        catch (OperationCanceledException) when (_downloadCancellation.IsCancellationRequested)
        {
            StatusText.Text = "下载已取消，临时文件已清理。";
            SetDownloading(false);
        }
        catch (Exception exception)
        {
            _reportError?.Invoke(exception);
            StatusText.Text = $"更新失败：{exception.Message}";
            SetDownloading(false);
        }
        finally
        {
            _downloadCancellation.Dispose();
            _downloadCancellation = null;
        }
    }

    private void ShowProgress(UpdateDownloadProgress progress)
    {
        ProgressPanel.Visibility = System.Windows.Visibility.Visible;
        RouteText.Text = progress.RequestUrl;
        switch (progress.Stage)
        {
            case UpdateDownloadStage.Connecting:
                DownloadProgress.IsIndeterminate = true;
                StatusText.Text = progress.Attempt == 1
                    ? $"正在连接下载线路 {progress.Attempt}/{progress.TotalAttempts}..."
                    : $"正在尝试备用线路 {progress.Attempt}/{progress.TotalAttempts}...";
                break;
            case UpdateDownloadStage.RouteFailed:
                DownloadProgress.IsIndeterminate = true;
                StatusText.Text = progress.Attempt < progress.TotalAttempts
                    ? $"线路 {progress.Attempt}/{progress.TotalAttempts} 失败，即将尝试下一线路：{progress.Error}"
                    : $"线路 {progress.Attempt}/{progress.TotalAttempts} 失败：{progress.Error}";
                break;
            case UpdateDownloadStage.Downloading:
                if (progress.TotalBytes is > 0)
                {
                    var percent = Math.Clamp(progress.BytesDownloaded * 100d / progress.TotalBytes.Value, 0, 99);
                    DownloadProgress.IsIndeterminate = false;
                    DownloadProgress.Value = percent;
                    StatusText.Text = $"正在下载... {percent:0}%  ·  {FormatBytes(progress.BytesDownloaded)} / {FormatBytes(progress.TotalBytes.Value)}";
                }
                else
                {
                    DownloadProgress.IsIndeterminate = true;
                    StatusText.Text = $"正在下载... 已接收 {FormatBytes(progress.BytesDownloaded)}";
                }
                break;
            case UpdateDownloadStage.VerifyingHash:
                DownloadProgress.IsIndeterminate = true;
                StatusText.Text = "下载完成，正在校验 SHA-256...";
                break;
            case UpdateDownloadStage.VerifyingPackage:
                DownloadProgress.IsIndeterminate = true;
                StatusText.Text = "哈希校验通过，正在校验版本、通道和包结构...";
                break;
            case UpdateDownloadStage.Ready:
                DownloadProgress.IsIndeterminate = false;
                DownloadProgress.Value = 100;
                StatusText.Text = "全部校验通过，正在请求管理员权限并启动更新器...";
                break;
        }
    }

    internal void ShowDownloadProgressForVisualQa(UpdateDownloadProgress progress)
    {
        ProgressPanel.Visibility = System.Windows.Visibility.Visible;
        SkipButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        InstallButton.Content = "取消下载";
        ShowProgress(progress);
    }

    internal double DownloadProgressValueForVisualQa => DownloadProgress.Value;

    private void Skip_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Selection = UpdatePromptSelection.Skip;
        DialogResult = true;
    }

    private void Later_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Selection = UpdatePromptSelection.Later;
        DialogResult = false;
    }

    private void SetDownloading(bool downloading)
    {
        _downloadInProgress = downloading;
        ProgressPanel.Visibility = System.Windows.Visibility.Visible;
        SkipButton.IsEnabled = !downloading;
        LaterButton.IsEnabled = !downloading;
        InstallButton.IsEnabled = true;
        InstallButton.Content = downloading ? "取消下载" : "重新下载";
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_downloadInProgress) return;
        e.Cancel = true;
        InstallButton.IsEnabled = false;
        StatusText.Text = "正在取消下载...";
        _downloadCancellation?.Cancel();
    }

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024
        ? $"{bytes / 1024d / 1024d:0.0} MB"
        : $"{bytes / 1024d:0.0} KB";

    private sealed class WindowProgress(UpdatePromptWindow owner) : IProgress<UpdateDownloadProgress>
    {
        public void Report(UpdateDownloadProgress value)
        {
            if (owner.Dispatcher.CheckAccess()) owner.ShowProgress(value);
            else owner.Dispatcher.Invoke(() => owner.ShowProgress(value));
        }
    }
}
