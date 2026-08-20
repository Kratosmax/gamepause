namespace GamePause.App;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var startupStore = new GamePause.Core.SessionStore();
        startupStore.Log("Application process started.");
        if (!EnsureAdministrator(args, startupStore)) return;

        UpdaterMaintenance.TryComplete(args, startupStore);
        if (!UpdateService.CleanupDownloadedPackages())
            startupStore.Log("Unable to remove downloaded update packages during startup cleanup.");
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            startupStore.Log($"Unhandled process exception; terminating={eventArgs.IsTerminating}: {eventArgs.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            startupStore.Log($"Unobserved task exception: {eventArgs.Exception}");
        using var mutex = new Mutex(true, "Global\\GamePause.SingleInstance", out var isFirstInstance);
        startupStore.Log($"Single-instance mutex created; first instance: {isFirstInstance}.");
        if (!isFirstInstance)
        {
            System.Windows.MessageBox.Show(
                "Game Pause 已经在运行。请检查系统托盘。",
                "Game Pause",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var application = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose
        };
        application.DispatcherUnhandledException += (_, eventArgs) =>
        {
            startupStore.Log($"Unhandled WPF exception: {eventArgs.Exception}");
            System.Windows.MessageBox.Show(
                "Game Pause 遇到未处理错误，详细信息已写入 game-pause.log。程序将退出，守护进程会检查暂停恢复记录。",
                "Game Pause 错误",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        };
        startupStore.Log("WPF initialized; creating main window.");
        application.Run(MainWindow.CreateForStartup(args.Contains("--silent", StringComparer.OrdinalIgnoreCase)));
        startupStore.Log("Application message loop exited.");
    }

    private static bool EnsureAdministrator(string[] args, GamePause.Core.SessionStore startupStore)
    {
        if (ElevationService.IsAdministrator()) return true;

        if (args.Contains("--elevation-attempted", StringComparer.OrdinalIgnoreCase))
        {
            startupStore.Log("Elevation restart completed without administrator privileges; exiting.");
            System.Windows.MessageBox.Show(
                "未能获得管理员权限，Game Pause 无法启动。请检查 Windows 用户账户控制或系统策略。",
                "无法以管理员身份启动",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            return false;
        }

        startupStore.Log("Administrator privileges are required; showing elevation prompt.");
        if (new ElevationPromptWindow().ShowDialog() != true)
        {
            startupStore.Log("User cancelled the administrator restart prompt.");
            return false;
        }

        var result = ElevationService.RestartAsAdministrator(args, out var error);
        startupStore.Log($"Administrator restart result: {result}; error: {error ?? "none"}.");
        if (result is ElevationLaunchResult.Started or ElevationLaunchResult.Cancelled) return false;

        System.Windows.MessageBox.Show(
            $"无法以管理员身份重新启动 Game Pause。\n\n{error ?? "Windows 未返回详细原因。"}",
            "启动失败",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
        return false;
    }
}
