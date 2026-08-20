using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GamePause.App;
using GamePause.Core;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Environment.SetEnvironmentVariable(
            "windir",
            Environment.GetEnvironmentVariable("SystemRoot") ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        if (args.Contains("--settings-test", StringComparer.OrdinalIgnoreCase))
        {
            RunSettingsTest();
            return 0;
        }

        AppContext.SetSwitch("GamePause.DisableBackdrop", true);
        var application = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        var outputDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var window = new MainWindow(true);
        window.Show();
        application.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        if (!window.VerifyDisplayColumnsRejectEditing())
        {
            throw new InvalidOperationException("A display-only DataGrid column still permits editing.");
        }

        Render(window, Path.Combine(outputDirectory, "main-window-wpf.png"), 1080, 780);
        Render(window, Path.Combine(outputDirectory, "main-window-wpf-minimum.png"), 920, 660);
        window.ShowPausedVisualQa();
        Render(window, Path.Combine(outputDirectory, "main-window-wpf-paused-minimum.png"), 920, 660);
        window.ShowProfilesVisualQa();
        Render(window, Path.Combine(outputDirectory, "main-window-wpf-profiles-minimum.png"), 920, 660);

        var profileWindow = new GameProfileWindow(new GameProfile(
            Guid.NewGuid(), "黑神话：悟空", "b1-Win64-Shipping", "D:\\Games\\BlackMythWukong\\b1-Win64-Shipping.exe",
            GamePauseMode.Deep, true, 10, true, false));
        profileWindow.Show();
        application.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Render(profileWindow, Path.Combine(outputDirectory, "game-profile-window-wpf.png"), 520, 610);
        profileWindow.Close();

        var settingsWindow = new HotkeySettingsWindow(
            HotkeySettings.Default,
            true,
            "1.1.1",
            new UpdateNetworkSettings([
                new GithubProxySetting(string.Empty, 8, true),
                new GithubProxySetting("https://gh-proxy.org", 10),
                new GithubProxySetting("https://example.com/github", 5)
            ], "http://127.0.0.1:7890"));
        settingsWindow.Show();
        application.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Render(settingsWindow, Path.Combine(outputDirectory, "settings-window-general-wpf.png"), 720, 610);
        settingsWindow.ShowNetworkSettingsForVisualQa();
        application.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Render(settingsWindow, Path.Combine(outputDirectory, "settings-window-wpf.png"), 720, 610);
        Render(settingsWindow, Path.Combine(outputDirectory, "settings-window-wpf-minimum.png"), 680, 610);
        settingsWindow.Close();

        var updateWindow = new UpdatePromptWindow("1.1.1", "1.2.0",
            "新增自动更新支持。\n修复进程暂停恢复稳定性问题。");
        updateWindow.Show();
        application.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Render(updateWindow, Path.Combine(outputDirectory, "update-window-wpf.png"), 510, 310);
        updateWindow.Close();

        window.Close();
        application.Shutdown();
        Console.WriteLine(outputDirectory);
        return 0;
    }

    private static void RunSettingsTest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GamePauseUiSettingsTests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new UiSettingsStore(directory);
            if (store.Load().CloseToTrayNoticeShown) throw new InvalidOperationException("Default preference must be false.");
            if (!store.Save(new UiSettings(true, "1.2.3")))
                throw new InvalidOperationException("UI settings could not be saved.");
            var reloaded = new UiSettingsStore(directory).Load();
            if (!reloaded.CloseToTrayNoticeShown) throw new InvalidOperationException("Saved preference was not reloaded.");
            if (reloaded.SkippedUpdateVersion != "1.2.3") throw new InvalidOperationException("Skipped update version was not reloaded.");

            var networkSettings = new UpdateNetworkSettings([
                new GithubProxySetting("https://proxy-a.example", 10),
                new GithubProxySetting(string.Empty, 8, true),
                new GithubProxySetting("https://proxy-b.example/base/", 5),
                new GithubProxySetting("https://disabled.example", 0)
            ], "http://127.0.0.1:7890");
            if (!store.Save(new UiSettings(true, "1.2.3", networkSettings)))
                throw new InvalidOperationException("Network settings could not be saved.");
            var savedNetwork = new UiSettingsStore(directory).Load().EffectiveUpdateNetwork;
            if (savedNetwork.HttpProxy != "http://127.0.0.1:7890")
                throw new InvalidOperationException("HTTP proxy was not reloaded.");
            if (savedNetwork.GithubProxies?.Count != 4
                || savedNetwork.GithubProxies.Single(item => item.IsDirect).Priority != 8)
                throw new InvalidOperationException("GitHub routes were not reloaded.");

            var urls = UpdateService.BuildRequestUrls(UpdateService.ManifestUrl, savedNetwork);
            var expectedUrls = new[]
            {
                $"https://proxy-a.example/{UpdateService.ManifestUrl}",
                UpdateService.ManifestUrl,
                $"https://proxy-b.example/base/{UpdateService.ManifestUrl}"
            };
            if (!urls.SequenceEqual(expectedUrls))
                throw new InvalidOperationException("GitHub route ordering or disabled-route handling is incorrect.");
            const string nonGithubUrl = "https://example.org/latest.json";
            if (!UpdateService.BuildRequestUrls(nonGithubUrl, savedNetwork).SequenceEqual([nonGithubUrl]))
                throw new InvalidOperationException("A non-GitHub URL was rewritten.");

            var normalizedWithoutDirect = new UpdateNetworkSettings([
                new GithubProxySetting("https://proxy-only.example", 7)
            ]).Normalize();
            if (normalizedWithoutDirect.GithubProxies?.Count(item => item.IsDirect) != 1)
                throw new InvalidOperationException("The permanent direct GitHub route was not restored.");
            var allDisabled = new UpdateNetworkSettings([
                new GithubProxySetting(string.Empty, 0, true),
                new GithubProxySetting("https://disabled.example", 0)
            ]);
            if (UpdateService.BuildRequestUrls(UpdateService.ManifestUrl, allDisabled).Count != 0)
                throw new InvalidOperationException("Disabled GitHub routes were used.");
            if (UpdateNetworkSettings.TryNormalizeGithubProxy("https://user:pass@proxy.example", out _)
                || UpdateNetworkSettings.TryNormalizeGithubProxy("https://proxy.example/?token=secret", out _)
                || UpdateNetworkSettings.TryNormalizeHttpProxy("https://127.0.0.1:7890", out _))
                throw new InvalidOperationException("Invalid or credential-bearing proxy settings were accepted.");

            var legacyDirectory = Path.Combine(directory, "legacy");
            Directory.CreateDirectory(legacyDirectory);
            File.WriteAllText(Path.Combine(legacyDirectory, "ui-settings.json"),
                "{\"CloseToTrayNoticeShown\":true,\"SkippedUpdateVersion\":\"1.0.1\"}");
            var legacySettings = new UiSettingsStore(legacyDirectory).Load();
            if (!legacySettings.CloseToTrayNoticeShown
                || legacySettings.EffectiveUpdateNetwork.GithubProxies?.Single().IsDirect != true)
                throw new InvalidOperationException("Legacy UI settings are not backward compatible.");
            var profileStore = new GameProfileStore(directory);
            profileStore.Save([
                new GameProfile(Guid.NewGuid(), "Long Test Game", "test-game", "C:\\Games\\test-game.exe",
                    GamePauseMode.Deep, true, 12, true),
                new GameProfile(Guid.NewGuid(), "Second Game", "second-game", "D:\\Games\\second-game.exe",
                    GamePauseMode.Standard, false, 10, false)
            ]);
            var profiles = new GameProfileStore(directory).Load();
            if (profiles.Count != 2) throw new InvalidOperationException("Multiple game profiles were not reloaded.");
            var profile = profiles.Single(item => item.ProcessName == "test-game");
            if (profile.PauseMode != GamePauseMode.Deep || !profile.AutoPauseEnabled || profile.FocusLossDelaySeconds != 12
                || !profile.AutoResumeEnabled)
            {
                throw new InvalidOperationException("Saved game profile was not reloaded.");
            }
            var hotkeyStore = new HotkeySettingsStore(directory);
            hotkeyStore.Save(HotkeySettings.Default);
            if (new HotkeySettingsStore(directory).Load() != HotkeySettings.Default)
            {
                throw new InvalidOperationException("Saved hotkey settings were not reloaded.");
            }
            var startupArguments = StartupTaskService.BuildCreateArguments("C:\\Game Pause\\GamePause.exe");
            if (!startupArguments.Contains("HIGHEST")
                || !startupArguments.Any(argument => argument.Contains("--silent", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Startup task arguments are incomplete.");
            }
            Console.WriteLine(File.ReadAllText(Path.Combine(directory, "ui-settings.json")));
            Console.WriteLine(File.ReadAllText(Path.Combine(directory, "profiles.json")));
            Console.WriteLine(File.ReadAllText(Path.Combine(directory, "settings.json")));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static void Render(System.Windows.Window window, string path, int width, int height)
    {
        window.Width = width;
        window.Height = height;
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var pixelWidth = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX);
        var pixelHeight = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
