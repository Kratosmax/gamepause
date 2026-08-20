using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GamePause.Core;
using Forms = System.Windows.Forms;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using WpfMessageBox = System.Windows.MessageBox;

namespace GamePause.App;

public partial class MainWindow : System.Windows.Window
{
    private const int WmQueryEndSession = 0x0011;
    private const int WmEndSession = 0x0016;

    private readonly SafetyPolicy _safetyPolicy = new();
    private readonly SessionStore _store;
    private readonly ForegroundWindowTracker _foregroundTracker = new();
    private readonly ProcessCatalog _catalog;
    private readonly ProcessSuspensionService _suspensionService;
    private readonly HotkeySettingsStore _hotkeyStore;
    private readonly UiSettingsStore _uiSettingsStore;
    private readonly GameProfileStore _profileStore;
    private readonly AutoRuleTracker _autoRuleTracker = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly HashSet<int> _checkedProcessIds = [];
    private readonly HashSet<Guid> _checkedPausedTargetIds = [];
    private readonly Forms.NotifyIcon _notifyIcon = new();
    private readonly bool _visualQa;
    private readonly bool _startHidden;
    private readonly List<GameProfile> _profiles;
    private readonly Forms.ToolStripMenuItem _trayProfilesMenu = new("游戏收藏");
    private readonly Forms.ToolStripMenuItem _trayPausedMenu = new("已暂停程序");
    private IReadOnlyList<WindowProcessInfo> _cachedProcesses = [];
    private IReadOnlyList<string> _runningProcessNames = [];
    private HotkeySettings _hotkeySettings;
    private UiSettings _uiSettings;
    private bool _closeToTrayNoticeShown;
    private bool _allowExit;
    private bool _operationInProgress;
    private bool _refreshingRows;
    private bool _toggleHotkeyRegistered;
    private bool _emergencyHotkeyRegistered;
    private bool _evaluatingAutoRules;
    private bool _updateCheckInProgress;
    private bool _manualUpdateCheckRequested;
    private nint _windowHandle;

    public MainWindow() : this(false, false) { }

    internal MainWindow(bool visualQa) : this(visualQa, false) { }

    internal static MainWindow CreateForStartup(bool startHidden) => new(false, startHidden);

    private MainWindow(bool visualQa, bool startHidden)
    {
        _visualQa = visualQa;
        _startHidden = startHidden;
        _allowExit = visualQa;
        _store = new SessionStore();
        _store.Log("WPF main window construction started.");
        _catalog = new ProcessCatalog(_safetyPolicy);
        _suspensionService = new ProcessSuspensionService(_catalog, new NativeProcessApi(), _safetyPolicy, _store);
        _hotkeyStore = new HotkeySettingsStore(_store.DataDirectory);
        _hotkeySettings = _hotkeyStore.Load();
        _uiSettingsStore = new UiSettingsStore(_store.DataDirectory);
        _uiSettings = _uiSettingsStore.Load();
        _closeToTrayNoticeShown = _uiSettings.CloseToTrayNoticeShown;
        _profileStore = new GameProfileStore(_store.DataDirectory);
        _profiles = _profileStore.Load().ToList();

        InitializeComponent();
        if (_startHidden)
        {
            WindowState = System.Windows.WindowState.Minimized;
            ShowInTaskbar = false;
            Opacity = 0;
        }
        DataContext = this;
        Icon = LoadApplicationIcon();
        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        Closed += MainWindow_Closed;
        _refreshTimer.Tick += (_, _) => RunUiTask(RefreshAndEvaluateRulesAsync);
        UpdateHotkeyLabel();
        if (!_visualQa) ConfigureTray();
        _store.Log("WPF main window construction completed.");
    }

    public ObservableCollection<AvailableProcessRow> AvailableProcesses { get; } = [];
    public ObservableCollection<PausedProcessRow> PausedProcesses { get; } = [];
    public ObservableCollection<GameProfileRow> ProfileRows { get; } = [];

    internal void PrepareVisualQaData()
    {
        _refreshingRows = true;
        AvailableProcesses.Clear();
        AvailableProcesses.Add(new AvailableProcessRow(
            true, true, "前台", "未见风险", "视觉验收示例", new SolidColorBrush(MediaColor.FromRgb(21, 128, 61)),
            "VeryLongGameProcessName-Win64-Shipping-Production.exe",
            12852,
            "这是一个用于验证超长窗口标题不会和 PID、内存列挤在一起的测试窗口 - 文本应当显示省略号",
            "490 MB", MediaBrushes.Honeydew, new SolidColorBrush(MediaColor.FromRgb(30, 41, 59)), null));
        AvailableProcesses.Add(new AvailableProcessRow(
            false, true, string.Empty, "待确认", "视觉验收示例", new SolidColorBrush(MediaColor.FromRgb(180, 83, 9)),
            "ApplicationFrameHost",
            29556,
            "设置",
            "61 MB", MediaBrushes.Transparent, new SolidColorBrush(MediaColor.FromRgb(30, 41, 59)), null));
        PausedProcesses.Clear();
        PausedProcesses.Add(new PausedProcessRow(
            Guid.NewGuid(), true,
            "VeryLongGameProcessName-Win64-Shipping-Production.exe",
            12852, "7 / 7", "08-20 10:30:45", "深度暂停 · 已回收 12.4 GB"));
        ProfileRows.Clear();
        ProfileRows.Add(new GameProfileRow(
            new GameProfile(Guid.NewGuid(), "超长收藏游戏名称视觉验收示例", "VeryLongGameProcessName-Win64-Shipping-Production",
                "C:\\Games\\VeryLongGameProcessName-Win64-Shipping-Production.exe", GamePauseMode.Deep, true, 10, true),
            "运行中"));
        ProcessCountLabel.Text = "2 项";
        PausedCountLabel.Text = "1 个暂停目标";
        SelectionLabel.Text = "已勾选 1 个程序";
        SelectionDetailLabel.Text = "VeryLongGameProcessName-Win64-Shipping-Production.exe";
        StateLabel.Text = "视觉验收：长文本列宽、截断和横向滚动";
        _refreshingRows = false;
    }

    internal void ShowPausedVisualQa() => MainTabs.SelectedIndex = 1;

    internal void ShowProfilesVisualQa() => MainTabs.SelectedIndex = 2;

    internal bool VerifyDisplayColumnsRejectEditing()
    {
        var availableOk = VerifyGridColumns(ProcessGrid, AvailableProcesses.First(), expectedEditableColumns: 1);
        MainTabs.SelectedIndex = 1;
        UpdateLayout();
        var pausedOk = VerifyGridColumns(PausedGrid, PausedProcesses.First(), expectedEditableColumns: 1);
        MainTabs.SelectedIndex = 2;
        UpdateLayout();
        var profilesOk = VerifyGridColumns(ProfilesGrid, ProfileRows.First(), expectedEditableColumns: 0);
        MainTabs.SelectedIndex = 0;
        UpdateLayout();
        return availableOk && pausedOk && profilesOk;
    }

    private static bool VerifyGridColumns(System.Windows.Controls.DataGrid grid, object row, int expectedEditableColumns)
    {
        if (grid.Columns.Count(column => !column.IsReadOnly) != expectedEditableColumns) return false;
        foreach (var column in grid.Columns.Where(column => column.IsReadOnly))
        {
            grid.SelectedItem = row;
            grid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(row, column);
            if (!grid.BeginEdit()) continue;
            grid.CancelEdit();
            return false;
        }
        return true;
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _windowHandle = new WindowInteropHelper(this).Handle;
        if (WpfBackdrop.TryApply(this))
        {
            RootSurface.Background = new SolidColorBrush(MediaColor.FromArgb(188, 242, 247, 250));
        }
        else
        {
            Background = new SolidColorBrush(MediaColor.FromRgb(239, 244, 248));
            RootSurface.Background = new SolidColorBrush(MediaColor.FromRgb(239, 244, 248));
        }

        if (HwndSource.FromHwnd(_windowHandle) is { } source) source.AddHook(WindowMessageHook);
        if (!_visualQa && !RegisterHotkeyPair(_hotkeySettings))
        {
            SetStatus("快捷键注册失败，可能与其他程序冲突；请打开设置。", StateTone.Warning);
        }
        UpdateHotkeyLabel();
    }

    private void MainWindow_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_visualQa)
        {
            PrepareVisualQaData();
            return;
        }

        _store.Log("WPF main window shown.");
        var recovery = _suspensionService.ReconcileActiveSession();
        SetStatus(recovery.Message, !recovery.Success ? StateTone.Error
            : recovery.Session is null ? StateTone.Neutral : StateTone.Warning);
        if (!WatchdogLauncher.Start(_store))
        {
            SetStatus("守护进程未启动；退出前请恢复所有程序。", StateTone.Warning);
        }
        RefreshAllLists();
        _refreshTimer.Start();
        _ = CheckForUpdatesAsync(userInitiated: false, this);
        if (_startHidden)
        {
            Hide();
            WindowState = System.Windows.WindowState.Normal;
            ShowInTaskbar = true;
            Opacity = 1;
        }
    }

    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == GlobalHotkeys.WmHotkey)
        {
            var hotkeyId = wParam.ToInt32();
            if (hotkeyId == GlobalHotkeys.ToggleId)
            {
                RunUiTask(TogglePauseAsync);
                handled = true;
            }
            else if (hotkeyId == GlobalHotkeys.EmergencyResumeId)
            {
                RunUiTask(EmergencyResumeAsync);
                handled = true;
            }
        }
        else if (message is WmQueryEndSession or WmEndSession)
        {
            _suspensionService.ResumeActiveSession();
        }
        return nint.Zero;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowExit)
        {
            e.Cancel = true;
            Hide();
            if (!_closeToTrayNoticeShown && !_visualQa)
            {
                _closeToTrayNoticeShown = true;
                _uiSettings = _uiSettings with { CloseToTrayNoticeShown = true };
                _uiSettingsStore.Save(_uiSettings);
                _notifyIcon.ShowBalloonTip(2500, "Game Pause", "程序仍在系统托盘运行；以后关闭窗口将不再提醒。", Forms.ToolTipIcon.Info);
            }
            return;
        }
        _refreshTimer.Stop();
        _notifyIcon.Visible = false;
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        UnregisterHotkeys();
        _notifyIcon.Dispose();
        _foregroundTracker.Dispose();
    }

    private void ConfigureTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Opening += (_, _) => RebuildTrayQuickMenus();
        menu.Items.Add("显示主界面", null, (_, _) => Dispatcher.Invoke(ShowMainWindow));
        menu.Items.Add(_trayProfilesMenu);
        menu.Items.Add(_trayPausedMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("暂停勾选 / 恢复全部", null, (_, _) => DispatchUiTask(TogglePauseAsync));
        menu.Items.Add("紧急全部恢复", null, (_, _) => DispatchUiTask(EmergencyResumeAsync));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => DispatchUiTask(ExitAsync));
        _notifyIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
        _notifyIcon.Text = "Game Pause";
        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowMainWindow);
    }

    private void RebuildTrayQuickMenus()
    {
        _trayProfilesMenu.DropDownItems.Clear();
        foreach (var profile in _profiles.OrderBy(profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var target = (_suspensionService.ActiveSession?.Targets ?? []).FirstOrDefault(item => GameProfileMatcher.Matches(profile, item));
            var process = _cachedProcesses.FirstOrDefault(item => GameProfileMatcher.Matches(profile, item));
            var item = target is not null
                ? new Forms.ToolStripMenuItem($"恢复 {profile.DisplayName}")
                : new Forms.ToolStripMenuItem(process is not null ? $"暂停 {profile.DisplayName}" : $"{profile.DisplayName}（未运行）");
            item.Enabled = target is not null || process is not null;
            item.Click += (_, _) => DispatchUiTask(async () =>
            {
                if (target is not null) await ResumeTargetAsync(target.TargetId);
                else if (process is not null) await PauseProfileAsync(profile, process, automatic: false);
            });
            _trayProfilesMenu.DropDownItems.Add(item);
        }
        if (_trayProfilesMenu.DropDownItems.Count == 0)
        {
            _trayProfilesMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("尚无游戏档案") { Enabled = false });
        }

        _trayPausedMenu.DropDownItems.Clear();
        foreach (var target in (_suspensionService.ActiveSession?.Targets ?? []).OrderBy(item => item.CreatedAt))
        {
            var elapsed = DateTimeOffset.Now - target.CreatedAt;
            var item = new Forms.ToolStripMenuItem($"恢复 {target.TargetName} · {FormatElapsed(elapsed)}");
            item.Click += (_, _) => DispatchUiTask(() => ResumeTargetAsync(target.TargetId));
            _trayPausedMenu.DropDownItems.Add(item);
        }
        if (_trayPausedMenu.DropDownItems.Count == 0)
        {
            _trayPausedMenu.DropDownItems.Add(new Forms.ToolStripMenuItem("没有暂停中的程序") { Enabled = false });
        }
    }

    private void CaptureButton_Click(object sender, System.Windows.RoutedEventArgs e) => RunUiTask(CaptureForegroundAsync);
    private void PauseButton_Click(object sender, System.Windows.RoutedEventArgs e) => RunUiTask(PauseCheckedAsync);
    private void DeepPauseButton_Click(object sender, System.Windows.RoutedEventArgs e) => RunUiTask(DeepPauseCheckedAsync);
    private void ResumeButton_Click(object sender, System.Windows.RoutedEventArgs e) => RunUiTask(ResumeCheckedAsync);
    private void EmergencyButton_Click(object sender, System.Windows.RoutedEventArgs e) => RunUiTask(EmergencyResumeAsync);
    private void RefreshButton_Click(object sender, System.Windows.RoutedEventArgs e) => RefreshAllLists();
    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ApplyProcessFilter();
    private void ForegroundOnlyCheck_Changed(object sender, System.Windows.RoutedEventArgs e) => ApplyProcessFilter();
    private void HotkeyButton_Click(object sender, System.Windows.RoutedEventArgs e) => RunUiTask(OpenSettingsAsync);
    private void HibernateButton_Click(object sender, System.Windows.RoutedEventArgs e) => HibernateComputer();
    private void AddProfileButton_Click(object sender, System.Windows.RoutedEventArgs e) => AddProfileFromCheckedProcess();
    private void EditProfileButton_Click(object sender, System.Windows.RoutedEventArgs e) => EditSelectedProfile();
    private void DeleteProfileButton_Click(object sender, System.Windows.RoutedEventArgs e) => DeleteSelectedProfile();
    private void LocateProfileButton_Click(object sender, System.Windows.RoutedEventArgs e) => LocateSelectedProfile();
    private void ToggleProfileButton_Click(object sender, System.Windows.RoutedEventArgs e) => RunUiTask(ToggleSelectedProfileAsync);

    private void AvailableCheck_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_refreshingRows || sender is not System.Windows.Controls.CheckBox { DataContext: AvailableProcessRow row }) return;
        row.IsChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
        if (row.Source is null) return;
        if (row.IsChecked) _checkedProcessIds.Add(row.ProcessId);
        else _checkedProcessIds.Remove(row.ProcessId);
        UpdateSelectionSummary();
    }

    private void PausedCheck_Changed(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_refreshingRows || sender is not System.Windows.Controls.CheckBox { DataContext: PausedProcessRow row }) return;
        row.IsChecked = (sender as System.Windows.Controls.CheckBox)?.IsChecked == true;
        if (row.IsChecked) _checkedPausedTargetIds.Add(row.TargetId);
        else _checkedPausedTargetIds.Remove(row.TargetId);
    }

    private async Task CaptureForegroundAsync()
    {
        WindowState = System.Windows.WindowState.Minimized;
        Hide();
        _notifyIcon.ShowBalloonTip(1000, "捕获前台", "请在 3 秒内切换到游戏窗口。", Forms.ToolTipIcon.Info);
        await Task.Delay(3000);
        var foreground = _catalog.GetForegroundProcess();
        ShowMainWindow();
        if (foreground is null || _safetyPolicy.IsProtected(foreground))
        {
            SetStatus("未捕获到可暂停的前台进程。", StateTone.Warning);
            return;
        }
        _checkedProcessIds.Add(foreground.ProcessId);
        RefreshAllLists();
        SetStatus($"已勾选 {KnownGames.GetDisplayName(foreground.Name)}。", StateTone.Success);
    }

    private async Task TogglePauseAsync()
    {
        if (_checkedProcessIds.Count > 0) { await PauseCheckedAsync(); return; }
        if (_suspensionService.ActiveSession is not null) { await EmergencyResumeAsync(); return; }
        var foreground = _catalog.GetForegroundProcess();
        if (foreground is null || _safetyPolicy.IsProtected(foreground))
        {
            SetStatus("没有勾选程序，也未找到可暂停的前台进程。", StateTone.Warning);
            return;
        }
        var foregroundInfo = new WindowProcessInfo(
            foreground.ProcessId, foreground.Name, string.Empty, 0, false, foreground.ExecutablePath);
        if (!ConfirmCompatibility([foregroundInfo])) return;
        SetBusy(true, "正在暂停前台进程树...");
        FinishOperation(await Task.Run(() => _suspensionService.SuspendTree(foreground.ProcessId)));
    }

    private async Task PauseCheckedAsync()
    {
        if (_operationInProgress) return;
        var processIds = _checkedProcessIds.ToArray();
        if (processIds.Length == 0) { SetStatus("请先在“可暂停进程”中勾选程序。", StateTone.Warning); return; }
        var selected = _cachedProcesses.Where(process => processIds.Contains(process.ProcessId)).ToArray();
        if (!ConfirmCompatibility(selected)) return;
        SetBusy(true, $"正在暂停 {processIds.Length} 个程序的进程树...");
        var result = await Task.Run(() => _suspensionService.SuspendTrees(processIds));
        if (result.Success) _checkedProcessIds.Clear();
        FinishOperation(result);
    }

    private async Task DeepPauseCheckedAsync()
    {
        if (_operationInProgress) return;
        var processIds = _checkedProcessIds.ToArray();
        if (processIds.Length == 0) { SetStatus("请先在“可暂停进程”中勾选程序。", StateTone.Warning); return; }
        var selected = _cachedProcesses.Where(process => processIds.Contains(process.ProcessId)).ToArray();
        if (!ConfirmCompatibility(selected)) return;
        var answer = WpfMessageBox.Show(
            "深度暂停会先冻结程序，再请求 Windows 回收其物理工作集。\n\n这不是可跨重启恢复的内存镜像；恢复时可能出现明显卡顿。是否继续？",
            "确认深度暂停", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
        if (answer != System.Windows.MessageBoxResult.Yes) return;
        SetBusy(true, $"正在深度暂停 {processIds.Length} 个程序...");
        var result = await Task.Run(() => _suspensionService.SuspendTrees(processIds, trimWorkingSets: true));
        if (result.Success) _checkedProcessIds.Clear();
        FinishOperation(result);
    }

    private async Task ResumeCheckedAsync()
    {
        if (_operationInProgress) return;
        var targetIds = _checkedPausedTargetIds.ToArray();
        if (targetIds.Length == 0) { SetStatus("请先在“已暂停程序”中勾选需要恢复的程序。", StateTone.Warning); return; }
        SetBusy(true, $"正在恢复 {targetIds.Length} 个程序...");
        var result = await Task.Run(() => _suspensionService.ResumeTargets(targetIds));
        if (result.Success) _checkedPausedTargetIds.Clear();
        FinishOperation(result);
    }

    private async Task EmergencyResumeAsync()
    {
        if (_operationInProgress) return;
        SetBusy(true, "正在执行紧急全部恢复...");
        var result = await Task.Run(_suspensionService.ResumeActiveSession);
        _checkedPausedTargetIds.Clear();
        FinishOperation(result);
    }

    private async Task ResumeTargetAsync(Guid targetId)
    {
        if (_operationInProgress) return;
        SetBusy(true, "正在恢复程序...");
        FinishOperation(await Task.Run(() => _suspensionService.ResumeTargets([targetId])));
    }

    private void FinishOperation(OperationResult result)
    {
        SetBusy(false, result.Message, result.Success ? StateTone.Success : StateTone.Error);
        RefreshAllLists();
        var activeCount = _suspensionService.ActiveSession?.Targets.Count ?? 0;
        _notifyIcon.Text = activeCount > 0 ? $"Game Pause - 已暂停 {activeCount} 个程序" : "Game Pause";
        RefreshProfileRows();
    }

    private async Task ExitAsync()
    {
        var activeCount = _suspensionService.ActiveSession?.Targets.Count ?? 0;
        if (_store.LastLoadError is not null)
        {
            ShowMainWindow();
            WpfMessageBox.Show(
                _store.LastLoadError + "\n\n无法确认是否仍有暂停进程。请先重启 Windows，再退出 Game Pause。",
                "无法安全退出", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return;
        }
        if (activeCount > 0)
        {
            ShowMainWindow();
            var answer = WpfMessageBox.Show(
                $"当前有 {activeCount} 个程序处于暂停状态。是否全部恢复后退出？\n\n选择“否”将取消退出。",
                "退出 Game Pause", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.Yes);
            if (answer != System.Windows.MessageBoxResult.Yes) return;
            var result = await Task.Run(_suspensionService.ResumeActiveSession);
            if (!result.Success)
            {
                WpfMessageBox.Show(result.Message + "\n请先使用紧急全部恢复。", "无法安全退出",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                RefreshAllLists();
                return;
            }
        }
        _allowExit = true;
        Close();
    }

    private void RefreshAllLists()
    {
        if (_operationInProgress || _visualQa) return;
        _cachedProcesses = _catalog.GetWindowProcesses();
        _runningProcessNames = _catalog.GetRunningProcessNames();
        _checkedProcessIds.IntersectWith(_cachedProcesses.Select(process => process.ProcessId));
        ApplyProcessFilter();
        RefreshPausedGrid();
        RefreshProfileRows();
    }

    private void ApplyProcessFilter()
    {
        if (!IsInitialized || _visualQa) return;
        var foregroundProcessId = _foregroundTracker.RelevantProcessId;
        var processes = ProcessListFilter.Apply(_cachedProcesses, SearchBox.Text, ForegroundOnlyCheck.IsChecked == true, foregroundProcessId);
        var pausedRootIds = (_suspensionService.ActiveSession?.Targets ?? []).Select(target => target.RootProcessId).ToHashSet();
        _refreshingRows = true;
        AvailableProcesses.Clear();
        foreach (var process in processes)
        {
            var isForeground = process.ProcessId == foregroundProcessId;
            var isPaused = pausedRootIds.Contains(process.ProcessId);
            var compatibility = CompatibilityChecker.Assess(process);
            var foreground = process.IsProtected
                ? new SolidColorBrush(MediaColor.FromRgb(148, 163, 184))
                : new SolidColorBrush(MediaColor.FromRgb(30, 41, 59));
            AvailableProcesses.Add(new AvailableProcessRow(
                _checkedProcessIds.Contains(process.ProcessId),
                compatibility.Rating != CompatibilityRating.Blocked && !isPaused,
                isPaused ? "已暂停" : isForeground ? "前台" : string.Empty,
                compatibility.Label,
                compatibility.Detail,
                CompatibilityBrush(compatibility.Rating),
                KnownGames.GetDisplayName(process.Name),
                process.ProcessId,
                process.WindowTitle,
                $"{process.WorkingSetBytes / 1024d / 1024d:N0} MB",
                isForeground ? new SolidColorBrush(MediaColor.FromRgb(220, 252, 231)) : MediaBrushes.Transparent,
                foreground,
                process));
        }
        _refreshingRows = false;
        ProcessCountLabel.Text = $"{processes.Count} 项";
        UpdateSelectionSummary();
    }

    private bool ConfirmCompatibility(IReadOnlyList<WindowProcessInfo> processes)
    {
        var assessments = processes.Select(process =>
            (Process: process, Assessment: CompatibilityChecker.Assess(
                process, _catalog.GetProcessTree(process.ProcessId), _runningProcessNames))).ToArray();
        var blocked = assessments.Where(item => item.Assessment.Rating == CompatibilityRating.Blocked).ToArray();
        if (blocked.Length > 0)
        {
            var detail = string.Join("\n", blocked.Select(item => $"• {KnownGames.GetDisplayName(item.Process.Name)}：{item.Assessment.Detail}"));
            WpfMessageBox.Show("以下程序已被兼容性检测阻止：\n\n" + detail, "无法暂停",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            return false;
        }

        var caution = assessments.Where(item => item.Assessment.Rating == CompatibilityRating.Caution).ToArray();
        if (caution.Length == 0) return true;
        var warning = string.Join("\n", caution.Select(item => $"• {KnownGames.GetDisplayName(item.Process.Name)}：{item.Assessment.Detail}"));
        return WpfMessageBox.Show(
            "兼容性检测发现以下注意项：\n\n" + warning + "\n\n仍要继续吗？",
            "兼容性提醒", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;
    }

    private async Task PauseProfileAsync(GameProfile profile, WindowProcessInfo process, bool automatic)
    {
        if (_operationInProgress) return;
        var assessment = CompatibilityChecker.Assess(process, _catalog.GetProcessTree(process.ProcessId), _runningProcessNames);
        if (automatic && assessment.Rating != CompatibilityRating.Clear
            && !(assessment.Rating == CompatibilityRating.Caution && profile.AllowCautionAutomaticRules))
        {
            SetStatus($"未自动暂停 {profile.DisplayName}：{assessment.Detail}", StateTone.Warning);
            return;
        }
        if (!automatic && !ConfirmCompatibility([process])) return;

        SetBusy(true, automatic ? $"正在按规则暂停 {profile.DisplayName}..." : $"正在暂停 {profile.DisplayName}...");
        var result = await Task.Run(() => _suspensionService.SuspendTrees(
            [process.ProcessId], profile.PauseMode == GamePauseMode.Deep));
        FinishOperation(result);
        if (automatic && result.Success)
        {
            _notifyIcon.ShowBalloonTip(1800, "自动暂停", $"{profile.DisplayName} 已失去前台并自动暂停。", Forms.ToolTipIcon.Info);
        }
    }

    private async Task RefreshAndEvaluateRulesAsync()
    {
        if (_evaluatingAutoRules || _operationInProgress || _visualQa) return;
        _evaluatingAutoRules = true;
        try
        {
            RefreshAllLists();
            var currentProcessId = _foregroundTracker.CurrentProcessId;
            if (currentProcessId == Environment.ProcessId)
            {
                foreach (var profile in _profiles) _autoRuleTracker.Reset(profile.Id);
                return;
            }

            foreach (var profile in _profiles.Where(item => item.AutoPauseEnabled))
            {
                var target = (_suspensionService.ActiveSession?.Targets ?? [])
                    .FirstOrDefault(item => GameProfileMatcher.Matches(profile, item));
                if (target is not null)
                {
                    _autoRuleTracker.Reset(profile.Id);
                    if (profile.AutoResumeEnabled && currentProcessId == target.RootProcessId)
                    {
                        await ResumeTargetAsync(target.TargetId);
                        _notifyIcon.ShowBalloonTip(1800, "自动恢复", $"{profile.DisplayName} 已回到前台并恢复。", Forms.ToolTipIcon.Info);
                        break;
                    }
                    continue;
                }

                var process = _cachedProcesses.FirstOrDefault(item => GameProfileMatcher.Matches(profile, item));
                var quickAssessment = process is null ? null : CompatibilityChecker.Assess(process);
                if (quickAssessment is not null
                    && quickAssessment.Rating != CompatibilityRating.Clear
                    && !(quickAssessment.Rating == CompatibilityRating.Caution && profile.AllowCautionAutomaticRules))
                {
                    _autoRuleTracker.Reset(profile.Id);
                    continue;
                }
                if (_autoRuleTracker.ShouldPause(
                        profile, process is not null, process?.ProcessId == currentProcessId, false, DateTimeOffset.Now)
                    && process is not null)
                {
                    await PauseProfileAsync(profile, process, automatic: true);
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            SetStatus($"自动规则检查失败：{exception.Message}", StateTone.Warning);
        }
        finally
        {
            _evaluatingAutoRules = false;
        }
    }

    private void RefreshPausedGrid()
    {
        var targets = _suspensionService.ActiveSession?.Targets ?? [];
        _checkedPausedTargetIds.IntersectWith(targets.Select(target => target.TargetId));
        _refreshingRows = true;
        PausedProcesses.Clear();
        foreach (var target in targets.OrderBy(target => target.CreatedAt))
        {
            var suspendedCount = target.Processes.Count(process => process.State is SuspensionState.Suspending or SuspensionState.Suspended);
            var deepProcesses = target.Processes.Where(process => process.DeepTrimRequested).ToArray();
            var releasedBytes = deepProcesses.Sum(process => Math.Max(0,
                (process.WorkingSetBeforeTrimBytes ?? 0) - (process.WorkingSetAfterTrimBytes ?? 0)));
            var status = deepProcesses.Length > 0
                ? releasedBytes > 0 ? $"深度 · 回收 {FormatBytes(releasedBytes)}" : "深度暂停"
                : "已暂停";
            PausedProcesses.Add(new PausedProcessRow(
                target.TargetId,
                _checkedPausedTargetIds.Contains(target.TargetId),
                target.TargetName,
                target.RootProcessId,
                $"{suspendedCount} / {target.Processes.Count}",
                target.CreatedAt.LocalDateTime.ToString("MM-dd HH:mm:ss"),
                suspendedCount > 0 ? status : "待检查"));
        }
        _refreshingRows = false;
        PausedCountLabel.Text = $"{targets.Count} 个暂停目标";
    }

    private void UpdateSelectionSummary()
    {
        SelectionLabel.Text = _checkedProcessIds.Count == 0 ? "尚未勾选程序" : $"已勾选 {_checkedProcessIds.Count} 个程序";
        var names = _cachedProcesses.Where(process => _checkedProcessIds.Contains(process.ProcessId))
            .Select(process => KnownGames.GetDisplayName(process.Name)).Take(3).ToArray();
        SelectionDetailLabel.Text = names.Length == 0
            ? "在“可暂停进程”中勾选一个或多个程序"
            : string.Join("、", names) + (_checkedProcessIds.Count > names.Length ? " 等" : string.Empty);
    }

    private void RefreshProfileRows()
    {
        if (!IsInitialized) return;
        var selectedId = (ProfilesGrid.SelectedItem as GameProfileRow)?.Profile.Id;
        var targets = _suspensionService.ActiveSession?.Targets ?? [];
        ProfileRows.Clear();
        foreach (var profile in _profiles.OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase))
        {
            var state = targets.Any(target => GameProfileMatcher.Matches(profile, target))
                ? "已暂停"
                : _cachedProcesses.Any(process => GameProfileMatcher.Matches(profile, process)) ? "运行中" : "未运行";
            ProfileRows.Add(new GameProfileRow(profile, state));
        }
        if (selectedId is not null)
        {
            ProfilesGrid.SelectedItem = ProfileRows.FirstOrDefault(row => row.Profile.Id == selectedId);
        }
    }

    private void AddProfileFromCheckedProcess()
    {
        var selected = _cachedProcesses.Where(process => _checkedProcessIds.Contains(process.ProcessId)).ToArray();
        if (selected.Length == 0)
        {
            WpfMessageBox.Show("请先在“可暂停进程”中勾选一个或多个程序。", "游戏档案",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        if (selected.Length == 1)
        {
            var process = selected[0];
            var assessment = CompatibilityChecker.Assess(process, _catalog.GetProcessTree(process.ProcessId), _runningProcessNames);
            if (assessment.Rating == CompatibilityRating.Blocked)
            {
                WpfMessageBox.Show(assessment.Detail, "无法创建档案", System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }
            var existing = _profiles.FirstOrDefault(profile => GameProfileMatcher.Matches(profile, process));
            var profile = existing ?? CreateDefaultProfile(process);
            EditProfile(profile, existing is not null);
            return;
        }

        var added = new List<GameProfile>();
        var duplicateCount = 0;
        var blockedCount = 0;
        foreach (var process in selected)
        {
            if (_profiles.Any(profile => GameProfileMatcher.Matches(profile, process)))
            {
                duplicateCount++;
                continue;
            }
            var assessment = CompatibilityChecker.Assess(process, _catalog.GetProcessTree(process.ProcessId), _runningProcessNames);
            if (assessment.Rating == CompatibilityRating.Blocked)
            {
                blockedCount++;
                continue;
            }
            var profile = CreateDefaultProfile(process);
            _profiles.Add(profile);
            added.Add(profile);
        }

        if (added.Count > 0)
        {
            SaveProfiles($"已批量创建 {added.Count} 个游戏档案。");
            MainTabs.SelectedIndex = 2;
            ProfilesGrid.SelectedItem = ProfileRows.FirstOrDefault(row => row.Profile.Id == added[0].Id);
        }

        var summary = $"已按安全默认值创建 {added.Count} 个档案：普通暂停、自动规则关闭。"
                      + $"\n跳过重复项 {duplicateCount} 个，兼容性阻止项 {blockedCount} 个。";
        if (added.Count > 0) summary += "\n\n请在游戏档案页逐个选择并点击“编辑”，完善暂停方式和自动规则。";
        WpfMessageBox.Show(summary, "批量创建游戏档案", System.Windows.MessageBoxButton.OK,
            added.Count > 0 ? System.Windows.MessageBoxImage.Information : System.Windows.MessageBoxImage.Warning);
    }

    private static GameProfile CreateDefaultProfile(WindowProcessInfo process) => new(
        Guid.NewGuid(), KnownGames.GetDisplayName(process.Name), process.Name, process.ExecutablePath,
        GamePauseMode.Standard, false, 10, false);

    private void EditSelectedProfile()
    {
        if (ProfilesGrid.SelectedItem is not GameProfileRow row)
        {
            SetStatus("请先选择一个游戏档案。", StateTone.Warning);
            return;
        }
        EditProfile(row.Profile, replaceExisting: true);
    }

    private void EditProfile(GameProfile profile, bool replaceExisting)
    {
        var dialog = new GameProfileWindow(profile) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedProfile is null) return;
        if (replaceExisting) _profiles.RemoveAll(item => item.Id == profile.Id);
        _profiles.Add(dialog.SelectedProfile);
        SaveProfiles("游戏档案已保存。");
    }

    private void DeleteSelectedProfile()
    {
        if (ProfilesGrid.SelectedItem is not GameProfileRow row)
        {
            SetStatus("请先选择一个游戏档案。", StateTone.Warning);
            return;
        }
        if (WpfMessageBox.Show($"确定删除“{row.DisplayName}”档案吗？\n这不会结束或恢复游戏进程。", "删除游戏档案",
                System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question,
                System.Windows.MessageBoxResult.No) != System.Windows.MessageBoxResult.Yes) return;
        _profiles.RemoveAll(profile => profile.Id == row.Profile.Id);
        _autoRuleTracker.Reset(row.Profile.Id);
        SaveProfiles("游戏档案已删除。");
    }

    private void LocateSelectedProfile()
    {
        if (ProfilesGrid.SelectedItem is not GameProfileRow row)
        {
            SetStatus("请先选择一个游戏档案。", StateTone.Warning);
            return;
        }
        var process = _cachedProcesses.FirstOrDefault(item => GameProfileMatcher.Matches(row.Profile, item));
        if (process is null)
        {
            SetStatus($"未找到正在运行的 {row.DisplayName}。", StateTone.Warning);
            return;
        }
        _checkedProcessIds.Add(process.ProcessId);
        MainTabs.SelectedIndex = 0;
        SearchBox.Clear();
        ForegroundOnlyCheck.IsChecked = false;
        ApplyProcessFilter();
        if (AvailableProcesses.FirstOrDefault(item => item.ProcessId == process.ProcessId) is { } rowToShow)
        {
            ProcessGrid.ScrollIntoView(rowToShow);
        }
        SetStatus($"已定位并勾选 {row.DisplayName}。", StateTone.Success);
    }

    private async Task ToggleSelectedProfileAsync()
    {
        if (ProfilesGrid.SelectedItem is not GameProfileRow row)
        {
            SetStatus("请先选择一个游戏档案。", StateTone.Warning);
            return;
        }
        var target = (_suspensionService.ActiveSession?.Targets ?? [])
            .FirstOrDefault(item => GameProfileMatcher.Matches(row.Profile, item));
        if (target is not null)
        {
            await ResumeTargetAsync(target.TargetId);
            return;
        }
        var process = _cachedProcesses.FirstOrDefault(item => GameProfileMatcher.Matches(row.Profile, item));
        if (process is null)
        {
            SetStatus($"未找到正在运行的 {row.DisplayName}。", StateTone.Warning);
            return;
        }
        await PauseProfileAsync(row.Profile, process, automatic: false);
    }

    private void SaveProfiles(string successMessage)
    {
        try
        {
            _profileStore.Save(_profiles);
            RefreshProfileRows();
            SetStatus(successMessage, StateTone.Success);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus($"游戏档案保存失败：{exception.Message}", StateTone.Error);
        }
    }

    private async Task OpenSettingsAsync()
    {
        var startupEnabled = await Task.Run(StartupTaskService.IsEnabled);
        var dialog = new HotkeySettingsWindow(
            _hotkeySettings,
            startupEnabled,
            UpdateService.CurrentVersionText,
            _uiSettings.EffectiveUpdateNetwork,
            (owner, networkSettings) => CheckForUpdatesAsync(userInitiated: true, owner, networkSettings)) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (!TryApplyHotkeySettings(dialog.SelectedSettings))
        {
            WpfMessageBox.Show("快捷键注册失败：组合键可能已被其他程序占用。原快捷键已恢复。", "快捷键冲突",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }
        string? hotkeySaveError = null;
        try
        {
            _hotkeyStore.Save(_hotkeySettings);
            SetStatus("快捷键已保存。", StateTone.Success);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            hotkeySaveError = exception.Message;
            SetStatus($"快捷键已生效，但保存失败：{exception.Message}", StateTone.Warning);
        }
        UpdateHotkeyLabel();

        _uiSettings = _uiSettings with { UpdateNetwork = dialog.SelectedUpdateNetworkSettings };
        if (!_uiSettingsStore.Save(_uiSettings))
        {
            WpfMessageBox.Show("网络与更新设置已在本次运行中生效，但无法写入 ui-settings.json。", "设置保存失败",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            SetStatus("部分设置无法保存到磁盘。", StateTone.Warning);
        }

        if (dialog.StartupEnabled != startupEnabled)
        {
            var startupResult = await Task.Run(() => StartupTaskService.SetEnabled(dialog.StartupEnabled));
            if (!startupResult.Success)
            {
                WpfMessageBox.Show(startupResult.Message, "开机启动设置失败",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                SetStatus("快捷键已保存，但开机启动设置失败。", StateTone.Warning);
                return;
            }
            SetStatus(hotkeySaveError is null
                ? "设置已保存。" + startupResult.Message
                : $"开机启动已更新，但快捷键保存失败：{hotkeySaveError}",
                hotkeySaveError is null ? StateTone.Success : StateTone.Warning);
        }
    }

    private async Task CheckForUpdatesAsync(
        bool userInitiated,
        System.Windows.Window dialogOwner,
        UpdateNetworkSettings? networkSettings = null)
    {
        if (_updateCheckInProgress)
        {
            if (userInitiated)
            {
                _manualUpdateCheckRequested = true;
                SetStatus("正在检查更新，请稍候...", StateTone.Neutral);
            }
            return;
        }

        _updateCheckInProgress = true;
        _manualUpdateCheckRequested = userInitiated;
        var updateAccepted = false;
        var effectiveNetworkSettings = (networkSettings ?? _uiSettings.EffectiveUpdateNetwork).Normalize();
        try
        {
            if (userInitiated) SetStatus("正在检查更新...", StateTone.Neutral);
            var update = await UpdateService.CheckAsync(effectiveNetworkSettings);
            var shouldReportResult = _manualUpdateCheckRequested;
            if (update is null)
            {
                if (shouldReportResult)
                {
                    SetStatus($"当前已是最新版本 {UpdateService.CurrentVersionText}。", StateTone.Success);
                    WpfMessageBox.Show($"当前已是最新版本 {UpdateService.CurrentVersionText}。", "检查更新",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
                return;
            }
            if (!shouldReportResult
                && string.Equals(_uiSettings.SkippedUpdateVersion, update.Version, StringComparison.OrdinalIgnoreCase)) return;

            var dialog = new UpdatePromptWindow(UpdateService.CurrentVersionText, update.Version, update.ReleaseNotes)
            {
                Owner = dialogOwner
            };
            dialog.ShowDialog();
            if (dialog.Selection == UpdatePromptSelection.Later) return;
            if (dialog.Selection == UpdatePromptSelection.Skip)
            {
                _uiSettings = _uiSettings with { SkippedUpdateVersion = update.Version };
                _uiSettingsStore.Save(_uiSettings);
                SetStatus($"已跳过版本 {update.Version}。", StateTone.Neutral);
                return;
            }

            updateAccepted = true;

            _ = _suspensionService.ActiveSession;
            if (_store.LastLoadError is not null)
            {
                WpfMessageBox.Show(
                    _store.LastLoadError + "\n\n无法确认暂停状态，因此本次更新已取消。请先重启 Windows。",
                    "无法安全更新", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return;
            }

            if ((_suspensionService.ActiveSession?.Targets.Count ?? 0) > 0)
            {
                var answer = WpfMessageBox.Show(
                    "更新前必须恢复当前暂停的所有程序。是否立即恢复并继续更新？",
                    "准备更新", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning,
                    System.Windows.MessageBoxResult.No);
                if (answer != System.Windows.MessageBoxResult.Yes) return;
                var resumeResult = await Task.Run(_suspensionService.ResumeActiveSession);
                if (!resumeResult.Success)
                {
                    WpfMessageBox.Show(resumeResult.Message, "无法安全更新", System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    RefreshAllLists();
                    return;
                }
            }

            SetStatus($"正在下载版本 {update.Version}...", StateTone.Neutral);
            var package = await UpdateService.DownloadAndPrepareAsync(update, effectiveNetworkSettings);
            if (!UpdateService.LaunchUpdater(package))
            {
                SetStatus("无法启动自动更新器，更新已取消。", StateTone.Error);
                return;
            }

            _allowExit = true;
            Close();
        }
        catch (Exception exception)
        {
            _store.Log($"Update check failed: {exception.Message}");
            if (updateAccepted)
            {
                SetStatus($"自动更新失败：{exception.Message}", StateTone.Error);
                WpfMessageBox.Show($"自动更新失败，当前版本未被替换。\n\n{exception.Message}", "更新失败",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            else if (_manualUpdateCheckRequested)
            {
                var checkMessage = exception is HttpRequestException or TaskCanceledException
                    ? $"无法连接服务器：{UpdateService.ManifestUrl}"
                    : $"检查更新失败：{exception.Message}";
                SetStatus(checkMessage, StateTone.Error);
                WpfMessageBox.Show(checkMessage, "检查更新失败",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }
        finally
        {
            _updateCheckInProgress = false;
            _manualUpdateCheckRequested = false;
        }
    }

    private bool TryApplyHotkeySettings(HotkeySettings settings)
    {
        var previous = _hotkeySettings;
        UnregisterHotkeys();
        if (!RegisterHotkeyPair(settings))
        {
            UnregisterHotkeys();
            RegisterHotkeyPair(previous);
            UpdateHotkeyLabel();
            return false;
        }
        _hotkeySettings = settings;
        return true;
    }

    private bool RegisterHotkeyPair(HotkeySettings settings)
    {
        if (_windowHandle == nint.Zero) return false;
        _toggleHotkeyRegistered = GlobalHotkeys.RegisterHotKey(
            _windowHandle, GlobalHotkeys.ToggleId, settings.Toggle.Modifiers | GlobalHotkeys.NoRepeat, (uint)settings.Toggle.Key);
        if (!_toggleHotkeyRegistered) return false;
        _emergencyHotkeyRegistered = GlobalHotkeys.RegisterHotKey(
            _windowHandle, GlobalHotkeys.EmergencyResumeId, settings.Emergency.Modifiers | GlobalHotkeys.NoRepeat, (uint)settings.Emergency.Key);
        if (_emergencyHotkeyRegistered) return true;
        GlobalHotkeys.UnregisterHotKey(_windowHandle, GlobalHotkeys.ToggleId);
        _toggleHotkeyRegistered = false;
        return false;
    }

    private void UnregisterHotkeys()
    {
        if (_windowHandle != nint.Zero)
        {
            if (_toggleHotkeyRegistered) GlobalHotkeys.UnregisterHotKey(_windowHandle, GlobalHotkeys.ToggleId);
            if (_emergencyHotkeyRegistered) GlobalHotkeys.UnregisterHotKey(_windowHandle, GlobalHotkeys.EmergencyResumeId);
        }
        _toggleHotkeyRegistered = false;
        _emergencyHotkeyRegistered = false;
    }

    private void UpdateHotkeyLabel() =>
        HotkeyLabel.Text = $"暂停/恢复：{_hotkeySettings.Toggle.DisplayText}    紧急恢复：{_hotkeySettings.Emergency.DisplayText}";

    private void DispatchUiTask(Func<Task> action) =>
        Dispatcher.BeginInvoke(new Action(() => RunUiTask(action)));

    private async void RunUiTask(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _store.Log($"UI operation failed: {exception}");
            SetBusy(false, $"操作失败：{exception.Message}", StateTone.Error);
            RefreshAllLists();
        }
    }

    private void SetBusy(bool busy, string message, StateTone tone = StateTone.Neutral)
    {
        _operationInProgress = busy;
        PauseButton.IsEnabled = !busy;
        DeepPauseButton.IsEnabled = !busy;
        ResumeButton.IsEnabled = !busy;
        EmergencyButton.IsEnabled = !busy;
        SetStatus(message, tone);
    }

    private void SetStatus(string message, StateTone tone)
    {
        StateLabel.Text = message;
        StateLabel.Foreground = tone switch
        {
            StateTone.Success => new SolidColorBrush(MediaColor.FromRgb(21, 128, 61)),
            StateTone.Warning => new SolidColorBrush(MediaColor.FromRgb(180, 83, 9)),
            StateTone.Error => new SolidColorBrush(MediaColor.FromRgb(185, 28, 28)),
            _ => new SolidColorBrush(MediaColor.FromRgb(71, 85, 105))
        };
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = System.Windows.WindowState.Normal;
        Activate();
    }

    private void HibernateComputer()
    {
        var activeCount = _suspensionService.ActiveSession?.Targets.Count ?? 0;
        var pausedText = activeCount > 0 ? $"当前 {activeCount} 个暂停目标会随整机状态一同保存。" : "当前没有由 Game Pause 暂停的目标。";
        var answer = WpfMessageBox.Show(
            "这会让整台电脑进入 Windows 休眠，并保存所有程序的系统状态。\n\n" + pausedText + "\n请先保存其他程序中尚未保存的工作。",
            "确认整机休眠", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Information, System.Windows.MessageBoxResult.No);
        if (answer != System.Windows.MessageBoxResult.Yes) return;
        _store.Log($"System hibernation requested; {activeCount} paused target(s) recorded.");
        try
        {
            if (!Forms.Application.SetSuspendState(Forms.PowerState.Hibernate, false, false))
            {
                var enableAnswer = WpfMessageBox.Show(
                    "Windows 当前未能进入休眠，通常是因为完整休眠功能尚未启用。\n\n是否启用完整休眠文件并重试？这会修改系统电源设置，并占用数 GB 系统盘空间。",
                    "启用 Windows 休眠", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No);
                if (enableAnswer != System.Windows.MessageBoxResult.Yes) { SetStatus("已取消；Windows 休眠尚未启用。", StateTone.Warning); return; }
                var enableResult = SystemPowerService.EnableFullHibernate();
                _store.Log($"Enable full hibernation result: {enableResult.Success}; {enableResult.Message}");
                if (!enableResult.Success) { SetStatus($"无法启用休眠：{enableResult.Message}", StateTone.Error); return; }
                if (!Forms.Application.SetSuspendState(Forms.PowerState.Hibernate, false, false))
                {
                    SetStatus("已启用休眠，但 Windows 仍拒绝进入休眠；可能受固件或虚拟化限制。", StateTone.Error);
                    return;
                }
            }
            SetStatus("系统已从休眠恢复。", StateTone.Success);
            RefreshAllLists();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus($"无法进入休眠：{exception.Message}", StateTone.Error);
        }
    }

    private static BitmapSource? LoadApplicationIcon()
    {
        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        return icon is null ? null : Imaging.CreateBitmapSourceFromHIcon(icon.Handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
    }

    private static string FormatBytes(long bytes)
    {
        const double gigabyte = 1024d * 1024d * 1024d;
        const double megabyte = 1024d * 1024d;
        return bytes >= gigabyte ? $"{bytes / gigabyte:N1} GB" : $"{bytes / megabyte:N0} MB";
    }

    private static string FormatElapsed(TimeSpan elapsed) => elapsed.TotalHours >= 1
        ? $"{(int)elapsed.TotalHours} 小时 {elapsed.Minutes} 分"
        : $"{Math.Max(0, elapsed.Minutes)} 分钟";

    private static MediaBrush CompatibilityBrush(CompatibilityRating rating) => rating switch
    {
        CompatibilityRating.Clear => new SolidColorBrush(MediaColor.FromRgb(21, 128, 61)),
        CompatibilityRating.Caution => new SolidColorBrush(MediaColor.FromRgb(180, 83, 9)),
        _ => new SolidColorBrush(MediaColor.FromRgb(185, 28, 28))
    };

    private enum StateTone { Neutral, Success, Warning, Error }
}

public sealed class AvailableProcessRow
{
    internal AvailableProcessRow(bool isChecked, bool canSelect, string state, string compatibility,
        string compatibilityDetail, MediaBrush compatibilityBrush, string processName, int processId,
        string windowTitle, string memory, MediaBrush rowBackground, MediaBrush foreground, WindowProcessInfo? source)
    {
        IsChecked = isChecked;
        CanSelect = canSelect;
        State = state;
        Compatibility = compatibility;
        CompatibilityDetail = compatibilityDetail;
        CompatibilityBrush = compatibilityBrush;
        ProcessName = processName;
        ProcessId = processId;
        WindowTitle = windowTitle;
        Memory = memory;
        RowBackground = rowBackground;
        Foreground = foreground;
        Source = source;
    }

    public bool IsChecked { get; set; }
    public bool CanSelect { get; }
    public string State { get; }
    public string Compatibility { get; }
    public string CompatibilityDetail { get; }
    public MediaBrush CompatibilityBrush { get; }
    public string ProcessName { get; }
    public int ProcessId { get; }
    public string WindowTitle { get; }
    public string Memory { get; }
    public MediaBrush RowBackground { get; }
    public MediaBrush Foreground { get; }
    internal WindowProcessInfo? Source { get; }
}

public sealed class GameProfileRow
{
    internal GameProfileRow(GameProfile profile, string currentState)
    {
        Profile = profile;
        CurrentState = currentState;
    }

    internal GameProfile Profile { get; }
    public string DisplayName => Profile.DisplayName;
    public string ProcessName => Profile.ProcessName;
    public string? ExecutablePath => Profile.ExecutablePath;
    public string PauseMode => Profile.PauseMode == GamePauseMode.Deep ? "深度暂停" : "普通暂停";
    public string AutomaticRule => Profile.AutoPauseEnabled
        ? $"失焦 {Profile.FocusLossDelaySeconds} 秒暂停"
          + (Profile.AutoResumeEnabled ? " · 回前台恢复" : string.Empty)
          + (Profile.AllowCautionAutomaticRules ? " · 允许谨慎项" : string.Empty)
        : "关闭";
    public string CurrentState { get; }
    public MediaBrush RowBackground => MediaBrushes.Transparent;
    public MediaBrush Foreground => new SolidColorBrush(MediaColor.FromRgb(30, 41, 59));
}

public sealed class PausedProcessRow
{
    internal PausedProcessRow(Guid targetId, bool isChecked, string name, int processId, string processCount, string since, string status)
    {
        TargetId = targetId;
        IsChecked = isChecked;
        Name = name;
        ProcessId = processId;
        ProcessCount = processCount;
        Since = since;
        Status = status;
    }

    internal Guid TargetId { get; }
    public bool IsChecked { get; set; }
    public string Name { get; }
    public int ProcessId { get; }
    public string ProcessCount { get; }
    public string Since { get; }
    public string Status { get; }
    public MediaBrush RowBackground => MediaBrushes.Transparent;
    public MediaBrush Foreground => new SolidColorBrush(MediaColor.FromRgb(30, 41, 59));
}
