using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using GamePause.Core;

namespace GamePause.Updater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase)) return RunSelfTest();
        ApplicationConfiguration.Initialize();
        var options = ParseArguments(args);
        if (!TryValidate(options, out var ownerProcessId, out var packagePath, out var targetDirectory,
                out var appName, out var version, out var downloadUrl, out var expectedSha256, out var signature))
        {
            ShowError("更新参数不完整，无法继续安装。");
            return 2;
        }

        try
        {
            WaitForOwner(ownerProcessId);
            using var packageStream = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var actualSha256 = Convert.ToHexString(SHA256.HashData(packageStream));
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("管理员更新器重新校验 ZIP 时发现哈希不一致。");
            if (!UpdateManifestSecurity.Verify(version, downloadUrl, expectedSha256, signature))
                throw new InvalidDataException("管理员更新器验证更新清单签名失败。");

            packageStream.Position = 0;
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
            ValidatePackageVersion(archive, version);
            var updaterEntry = archive.GetEntry("GamePause.Updater.exe")
                ?? throw new InvalidDataException("更新包缺少 GamePause.Updater.exe。");
            var nextUpdaterHash = ComputeEntrySha256(updaterEntry);
            var rollback = InstallWithRollback(archive, targetDirectory);
            var applicationPath = Path.Combine(targetDirectory, appName);
            if (!File.Exists(applicationPath)) throw new FileNotFoundException("更新后找不到主程序。", applicationPath);
            try
            {
                var startInfo = new ProcessStartInfo(applicationPath) { UseShellExecute = true };
                startInfo.ArgumentList.Add("--complete-updater");
                startInfo.ArgumentList.Add(Path.Combine(targetDirectory, "GamePause.Updater.next"));
                startInfo.ArgumentList.Add("--updater-sha256");
                startInfo.ArgumentList.Add(nextUpdaterHash);
                startInfo.ArgumentList.Add("--updater-owner");
                startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
                if (Process.Start(startInfo) is null) throw new InvalidOperationException("无法重新启动主程序。");
                rollback.Commit();
            }
            catch
            {
                rollback.RollBack();
                throw;
            }
            Log($"Update installed from {packagePath} to {targetDirectory}.");
            return 0;
        }
        catch (Exception exception)
        {
            Log($"Update failed: {exception}");
            ShowError("自动更新失败，原安装目录没有被删除。\n\n" + exception.Message);
            return 1;
        }
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index + 1 < args.Length; index += 2)
        {
            if (args[index].StartsWith("--", StringComparison.Ordinal)) result[args[index]] = args[index + 1];
        }
        return result;
    }

    private static bool TryValidate(
        IReadOnlyDictionary<string, string> options,
        out int ownerProcessId,
        out string packagePath,
        out string targetDirectory,
        out string appName,
        out string version,
        out string downloadUrl,
        out string expectedSha256,
        out string signature)
    {
        ownerProcessId = 0;
        packagePath = string.Empty;
        targetDirectory = string.Empty;
        appName = string.Empty;
        version = string.Empty;
        downloadUrl = string.Empty;
        expectedSha256 = string.Empty;
        signature = string.Empty;
        if (!options.TryGetValue("--owner", out var owner) || !int.TryParse(owner, out ownerProcessId)
            || !options.TryGetValue("--package", out var package)
            || !options.TryGetValue("--target", out var target)
            || !options.TryGetValue("--app", out var application)
            || !options.TryGetValue("--version", out var parsedVersion)
            || !options.TryGetValue("--url", out var parsedUrl)
            || !options.TryGetValue("--sha256", out var parsedSha256)
            || !options.TryGetValue("--signature", out var parsedSignature)) return false;

        packagePath = Path.GetFullPath(package);
        targetDirectory = Path.GetFullPath(target);
        appName = Path.GetFileName(application);
        version = parsedVersion;
        downloadUrl = parsedUrl;
        expectedSha256 = parsedSha256;
        signature = parsedSignature;
        return ownerProcessId > 0
               && File.Exists(packagePath)
               && Directory.Exists(targetDirectory)
               && IsUnderProgramFiles(targetDirectory)
               && expectedSha256.Length == 64 && expectedSha256.All(Uri.IsHexDigit)
               && string.Equals(Path.GetExtension(appName), ".exe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderProgramFiles(string path)
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles)) return false;
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(programFiles).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static void WaitForOwner(int ownerProcessId)
    {
        try
        {
            using var owner = Process.GetProcessById(ownerProcessId);
            if (!owner.WaitForExit(30_000)) throw new TimeoutException("主程序在 30 秒内没有退出。");
        }
        catch (ArgumentException)
        {
            // The application already exited.
        }
    }

    private static RollbackPlan InstallWithRollback(ZipArchive archive, string targetDirectory)
    {
        var sourceFiles = archive.Entries
            .Where(entry => !string.IsNullOrEmpty(entry.Name))
            .Select(entry => new UpdateFile(entry, MapTargetPath(entry.FullName)))
            .ToArray();
        if (sourceFiles.Select(file => file.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != sourceFiles.Length)
            throw new InvalidDataException("更新包包含重复文件路径。");

        var backupDirectory = Path.Combine(targetDirectory, ".gamepause-update-backup");
        if (Directory.Exists(backupDirectory)) Directory.Delete(backupDirectory, true);
        Directory.CreateDirectory(backupDirectory);
        var installedFiles = new List<InstalledFile>();

        foreach (var file in sourceFiles)
        {
            var target = ResolveTargetPath(targetDirectory, file.RelativePath);
            var backup = Path.Combine(backupDirectory, file.RelativePath);
            var existed = File.Exists(target);
            if (existed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(target, backup, true);
            }
            installedFiles.Add(new InstalledFile(target, backup, existed));
        }

        var rollback = new RollbackPlan(installedFiles, backupDirectory);
        try
        {
            foreach (var file in sourceFiles)
            {
                var target = ResolveTargetPath(targetDirectory, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                CopyEntryWithRetry(file.Entry, target);
            }
            return rollback;
        }
        catch
        {
            rollback.RollBack();
            throw;
        }
    }

    private static string MapTargetPath(string relativePath) =>
        string.Equals(relativePath.Replace('/', Path.DirectorySeparatorChar), "GamePause.Updater.exe", StringComparison.OrdinalIgnoreCase)
            ? "GamePause.Updater.next"
            : relativePath;

    private static void CopyEntryWithRetry(ZipArchiveEntry entry, string target)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var source = entry.Open();
                using var destination = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
                source.CopyTo(destination);
                destination.Flush(flushToDisk: true);
                return;
            }
            catch (IOException exception)
            {
                lastError = exception;
                Thread.Sleep(300);
            }
        }
        throw new IOException($"无法替换文件 {Path.GetFileName(target)}。", lastError);
    }

    private static string ResolveTargetPath(string targetDirectory, string relativePath)
    {
        var target = Path.GetFullPath(Path.Combine(targetDirectory, relativePath));
        if (!target.StartsWith(targetDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新包包含非法文件路径。");
        }
        return target;
    }

    private static void CopyWithRetry(string source, string target)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                File.Copy(source, target, true);
                return;
            }
            catch (IOException exception)
            {
                lastError = exception;
                Thread.Sleep(300);
            }
        }
        throw new IOException($"无法替换文件 {Path.GetFileName(target)}。", lastError);
    }

    private static void ValidatePackageVersion(ZipArchive archive, string version)
    {
        var entry = archive.GetEntry("GamePause.dll") ?? throw new InvalidDataException("更新包缺少 GamePause.dll。");
        using var stream = entry.Open();
        using var peReader = new PEReader(stream);
        var actual = peReader.GetMetadataReader().GetAssemblyDefinition().Version;
        if (!Version.TryParse(version.TrimStart('v', 'V'), out var expected)
            || actual.Major != expected.Major
            || actual.Minor != expected.Minor
            || Math.Max(0, actual.Build) != Math.Max(0, expected.Build))
            throw new InvalidDataException($"更新包版本 {actual} 与清单版本 {version} 不一致。");
    }

    private static string ComputeEntrySha256(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void ShowError(string message) => MessageBox.Show(message, "Game Pause 更新",
        MessageBoxButtons.OK, MessageBoxIcon.Error);

    private static void Log(string message)
    {
        try
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamePause");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "update.log"),
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}", Encoding.UTF8);
        }
        catch
        {
            // Updating must not fail only because the diagnostic log is unavailable.
        }
    }

    private static int RunSelfTest()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GamePauseUpdaterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "existing.txt"), "old");
            using var package = new MemoryStream();
            using (var writer = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
            {
                WriteTestEntry(writer, "existing.txt", "new");
                WriteTestEntry(writer, "added.txt", "added");
            }
            package.Position = 0;
            using (var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
            {
                var rollback = InstallWithRollback(archive, directory);
                if (File.ReadAllText(Path.Combine(directory, "existing.txt")) != "new"
                    || File.ReadAllText(Path.Combine(directory, "added.txt")) != "added") return 3;
                rollback.RollBack();
            }
            if (File.ReadAllText(Path.Combine(directory, "existing.txt")) != "old"
                || File.Exists(Path.Combine(directory, "added.txt"))) return 4;

            using var maliciousPackage = new MemoryStream();
            using (var writer = new ZipArchive(maliciousPackage, ZipArchiveMode.Create, leaveOpen: true))
                WriteTestEntry(writer, "../escape.txt", "blocked");
            maliciousPackage.Position = 0;
            using var maliciousArchive = new ZipArchive(maliciousPackage, ZipArchiveMode.Read);
            try
            {
                InstallWithRollback(maliciousArchive, directory);
                return 5;
            }
            catch (InvalidDataException)
            {
                Console.WriteLine("Updater install, rollback, and path traversal tests passed.");
                return 0;
            }
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch (IOException) { }
        }
    }

    private static void WriteTestEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private sealed record UpdateFile(ZipArchiveEntry Entry, string RelativePath);
    private sealed record InstalledFile(string TargetPath, string BackupPath, bool Existed);

    private sealed class RollbackPlan(IReadOnlyList<InstalledFile> files, string backupDirectory)
    {
        internal void Commit()
        {
            try { Directory.Delete(backupDirectory, true); } catch (IOException) { }
        }

        internal void RollBack()
        {
            var errors = new List<Exception>();
            foreach (var file in files)
            {
                try
                {
                    if (file.Existed) CopyWithRetry(file.BackupPath, file.TargetPath);
                    else File.Delete(file.TargetPath);
                }
                catch (Exception exception)
                {
                    errors.Add(exception);
                }
            }
            if (errors.Count > 0) throw new AggregateException("更新失败，且部分文件无法自动回滚。", errors);
            try { Directory.Delete(backupDirectory, true); } catch (IOException) { }
        }
    }
}
