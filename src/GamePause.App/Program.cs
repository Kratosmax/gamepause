namespace GamePause.App;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var startupStore = new GamePause.Core.SessionStore();
        startupStore.Log("Application process started.");
        startupStore.Log($"Administrator privileges: {ElevationService.IsAdministrator()}.");

        UpdaterMaintenance.TryComplete(args, startupStore);
        UpdaterMaintenance.TryCleanupBackup(startupStore);
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

}
