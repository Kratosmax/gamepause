using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using GamePause.Core;

namespace GamePause.App;

internal sealed record UpdateManifest(string Version, string DownloadUrl, string Sha256, string Signature, string? ReleaseNotes);
internal sealed record PreparedUpdate(string Version, string PackagePath, string ExpectedSha256, string DownloadUrl, string Signature);

internal static class UpdateService
{
    // Placeholder until the GitHub Releases repository and update feed are published.
    internal const string ManifestUrl = "https://example.invalid/game-pause/latest.json";
    private static readonly Version CurrentVersion = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);

    internal static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{Math.Max(0, CurrentVersion.Build)}";
    internal static bool IsSecureInstallLocation => IsUnderDirectory(AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));

    internal static async Task<UpdateManifest?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(ManifestUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
        if (manifest is null
            || string.IsNullOrWhiteSpace(manifest.Version)
            || string.IsNullOrWhiteSpace(manifest.DownloadUrl)
            || string.IsNullOrWhiteSpace(manifest.Sha256)
            || string.IsNullOrWhiteSpace(manifest.Signature)
            || !UpdateVersionPolicy.IsNewer(manifest.Version, CurrentVersion)
            || !IsHttpUrl(manifest.DownloadUrl)
            || manifest.Sha256.Length != 64
            || !manifest.Sha256.All(Uri.IsHexDigit))
        {
            return null;
        }
        if (!UpdateManifestSecurity.Verify(manifest.Version, manifest.DownloadUrl, manifest.Sha256, manifest.Signature))
        {
            throw new InvalidDataException("更新清单签名无效。");
        }
        return manifest;
    }

    internal static async Task<PreparedUpdate> DownloadAndPrepareAsync(
        UpdateManifest manifest,
        CancellationToken cancellationToken = default)
    {
        var updateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GamePause", "updates", manifest.Version);
        var packagePath = Path.Combine(updateRoot, "package.zip");
        var temporaryPath = packagePath + ".tmp";
        Directory.CreateDirectory(updateRoot);

        using (var client = CreateClient())
        using (var response = await client.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await source.CopyToAsync(destination, cancellationToken);
        }

        var actualHash = await ComputeSha256Async(temporaryPath, cancellationToken);
        if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(temporaryPath);
            throw new InvalidDataException("下载文件的 SHA-256 校验失败。更新已取消。");
        }

        File.Move(temporaryPath, packagePath, true);
        using (var archive = ZipFile.OpenRead(packagePath))
        {
            if (archive.GetEntry("GamePause.exe") is null
                || archive.GetEntry("GamePause.dll") is null
                || archive.GetEntry("GamePause.Updater.exe") is null)
            {
                throw new InvalidDataException("更新包缺少主程序或更新器。");
            }
            var packagedVersion = ReadAssemblyVersion(archive.GetEntry("GamePause.dll")!);
            if (!Version.TryParse(manifest.Version.TrimStart('v', 'V'), out var expectedVersion)
                || !SameReleaseVersion(packagedVersion, expectedVersion))
            {
                throw new InvalidDataException($"更新包版本 {packagedVersion} 与清单版本 {manifest.Version} 不一致。");
            }
        }
        return new PreparedUpdate(manifest.Version, packagePath, manifest.Sha256, manifest.DownloadUrl, manifest.Signature);
    }

    internal static bool LaunchUpdater(PreparedUpdate update)
    {
        var updaterPath = Path.Combine(AppContext.BaseDirectory, "GamePause.Updater.exe");
        var applicationPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(applicationPath)) return false;
        try
        {
            var startInfo = new ProcessStartInfo(updaterPath)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            startInfo.ArgumentList.Add("--owner");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add("--package");
            startInfo.ArgumentList.Add(update.PackagePath);
            startInfo.ArgumentList.Add("--sha256");
            startInfo.ArgumentList.Add(update.ExpectedSha256);
            startInfo.ArgumentList.Add("--version");
            startInfo.ArgumentList.Add(update.Version);
            startInfo.ArgumentList.Add("--url");
            startInfo.ArgumentList.Add(update.DownloadUrl);
            startInfo.ArgumentList.Add("--signature");
            startInfo.ArgumentList.Add(update.Signature);
            startInfo.ArgumentList.Add("--target");
            startInfo.ArgumentList.Add(AppContext.BaseDirectory);
            startInfo.ArgumentList.Add("--app");
            startInfo.ArgumentList.Add(Path.GetFileName(applicationPath));
            return Process.Start(startInfo) is not null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GamePause", CurrentVersionText));
        return client;
    }

    private static bool IsHttpUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";

    private static bool IsUnderDirectory(string path, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return false;
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static Version ReadAssemblyVersion(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata) throw new InvalidDataException("更新包中的 GamePause.dll 没有程序集元数据。");
        return peReader.GetMetadataReader().GetAssemblyDefinition().Version;
    }

    private static bool SameReleaseVersion(Version left, Version right) =>
        left.Major == right.Major && left.Minor == right.Minor && Math.Max(0, left.Build) == Math.Max(0, right.Build);
}
