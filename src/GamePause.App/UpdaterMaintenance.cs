using System.Diagnostics;
using System.Security.Cryptography;
using GamePause.Core;

namespace GamePause.App;

internal static class UpdaterMaintenance
{
    internal static void TryComplete(string[] args, SessionStore store)
    {
        var options = ParseArguments(args);
        if (!options.TryGetValue("--complete-updater", out var nextPath)
            || !options.TryGetValue("--updater-sha256", out var expectedHash)
            || !options.TryGetValue("--updater-owner", out var ownerText)
            || !int.TryParse(ownerText, out var ownerProcessId)) return;

        string? expectedPath = null;
        try
        {
            expectedPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "GamePause.Updater.next"));
            if (!string.Equals(Path.GetFullPath(nextPath), expectedPath, StringComparison.OrdinalIgnoreCase)
                || expectedHash.Length != 64 || !expectedHash.All(Uri.IsHexDigit)
                || ownerProcessId <= 0)
                throw new InvalidDataException("Pending updater completion arguments were invalid.");

            using var source = new FileStream(expectedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var actualHash = Convert.ToHexString(SHA256.HashData(source));
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pending updater hash validation failed.");
            source.Position = 0;
            WaitForProcess(ownerProcessId);
            var updaterPath = Path.Combine(AppContext.BaseDirectory, "GamePause.Updater.exe");
            using (var destination = new FileStream(updaterPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(destination);
                destination.Flush(flushToDisk: true);
            }
            source.Close();
            File.Delete(expectedPath);
            store.Log("Pending updater executable was installed successfully.");
            TryCleanupBackup(store);
        }
        catch (Exception exception)
        {
            store.Log($"Unable to complete updater replacement: {exception.Message}");
            if (exception is InvalidDataException && expectedPath is not null)
            {
                try { File.Delete(expectedPath); } catch (Exception deleteException) when (deleteException is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    internal static void TryCleanupBackup(SessionStore store)
    {
        var backupDirectory = Path.Combine(AppContext.BaseDirectory, ".gamepause-update-backup");
        try
        {
            if (!Directory.Exists(backupDirectory)) return;
            Directory.Delete(backupDirectory, true);
            store.Log("Update backup directory was cleaned successfully.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            store.Log($"Unable to clean update backup directory: {exception.Message}");
        }
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (args[index].StartsWith("--", StringComparison.Ordinal)) result[args[index]] = args[index + 1];
        }
        return result;
    }

    private static void WaitForProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.WaitForExit(30_000)) throw new IOException("Updater did not exit within 30 seconds.");
        }
        catch (ArgumentException)
        {
            // The updater already exited.
        }
    }
}
