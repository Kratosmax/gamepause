using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GamePause.Core;

namespace GamePause.App;

internal sealed record UpdateManifest(string Version, string DownloadUrl, string Sha256, string Signature, string? ReleaseNotes);
internal sealed record PreparedUpdate(string Version, string PackagePath, string ExpectedSha256, string DownloadUrl, string Signature);
internal enum UpdateDownloadStage { Connecting, RouteFailed, Downloading, VerifyingHash, VerifyingPackage, Ready }
internal sealed record UpdateDownloadProgress(
    UpdateDownloadStage Stage,
    string RequestUrl,
    long BytesDownloaded = 0,
    long? TotalBytes = null,
    int Attempt = 1,
    int TotalAttempts = 1,
    string? Error = null);

internal static class UpdateService
{
    internal const string DistributionChannelFileName = "distribution-channel.txt";
    internal const string FullDistributionChannel = "full";
    internal const string LiteDistributionChannel = "lite";
    internal const long MaxPackageBytes = 512L * 1024 * 1024;
    internal static readonly TimeSpan DownloadStallTimeout = TimeSpan.FromSeconds(30);
    internal static string DistributionChannel => ReadDistributionChannel(AppContext.BaseDirectory);
    internal static string ManifestUrl => GetManifestUrl(AppContext.BaseDirectory);
    private static readonly Version CurrentVersion = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);

    internal static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{Math.Max(0, CurrentVersion.Build)}";
    internal static async Task<UpdateManifest?> CheckAsync(
        UpdateNetworkSettings? networkSettings = null,
        CancellationToken cancellationToken = default)
    {
        var settings = (networkSettings ?? UpdateNetworkSettings.Default).Normalize();
        using var client = CreateClient(settings);
        Exception? lastError = null;
        var receivedValidCurrentManifest = false;

        foreach (var requestUrl in BuildRequestUrls(ManifestUrl, settings))
        {
            try
            {
                using var response = await client.GetAsync(requestUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    lastError = CreateStatusException(requestUrl, response.StatusCode);
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
                if (!IsStructurallyValid(manifest))
                {
                    lastError = new InvalidDataException($"更新服务器返回的清单格式无效：{requestUrl}");
                    continue;
                }
                if (!UpdateManifestSecurity.Verify(manifest!.Version, manifest.DownloadUrl, manifest.Sha256, manifest.Signature))
                {
                    lastError = new InvalidDataException($"更新清单签名无效：{requestUrl}");
                    continue;
                }
                if (UpdateVersionPolicy.IsNewer(manifest.Version, CurrentVersion)) return manifest;
                receivedValidCurrentManifest = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or JsonException)
            {
                lastError = exception;
            }
        }

        if (receivedValidCurrentManifest) return null;
        throw lastError ?? new HttpRequestException("所有更新服务器均无法访问。");
    }

    internal static async Task<PreparedUpdate> DownloadAndPrepareAsync(
        UpdateManifest manifest,
        UpdateNetworkSettings? networkSettings = null,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var settings = (networkSettings ?? UpdateNetworkSettings.Default).Normalize();
        var updateRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GamePause", "updates", manifest.Version);
        var packagePath = Path.Combine(updateRoot, "package.zip");
        var temporaryPath = packagePath + ".tmp";
        Directory.CreateDirectory(updateRoot);
        using var client = CreateClient(settings);
        Exception? lastError = null;

        var requestUrls = BuildRequestUrls(manifest.DownloadUrl, settings);
        for (var index = 0; index < requestUrls.Count; index++)
        {
            var requestUrl = requestUrls[index];
            var attempt = index + 1;
            progress?.Report(new UpdateDownloadProgress(
                UpdateDownloadStage.Connecting, requestUrl, Attempt: attempt, TotalAttempts: requestUrls.Count));
            try
            {
                using var response = await client.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    throw CreateStatusException(requestUrl, response.StatusCode);
                if (response.Content.Headers.ContentLength is > MaxPackageBytes)
                    throw new InvalidDataException($"更新包超过 {MaxPackageBytes / 1024 / 1024} MB 安全上限：{requestUrl}");
                var contentLength = response.Content.Headers.ContentLength;
                await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await CopyDownloadAsync(source, destination, MaxPackageBytes, DownloadStallTimeout,
                        cancellationToken, progress, requestUrl, contentLength, attempt, requestUrls.Count);
                    await destination.FlushAsync(cancellationToken);
                }

                progress?.Report(new UpdateDownloadProgress(
                    UpdateDownloadStage.VerifyingHash, requestUrl, Attempt: attempt, TotalAttempts: requestUrls.Count));
                var actualHash = await ComputeSha256Async(temporaryPath, cancellationToken);
                if (!string.Equals(actualHash, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"下载文件的 SHA-256 校验失败：{requestUrl}");

                progress?.Report(new UpdateDownloadProgress(
                    UpdateDownloadStage.VerifyingPackage, requestUrl, Attempt: attempt, TotalAttempts: requestUrls.Count));
                ValidatePackage(temporaryPath, manifest.Version);
                File.Move(temporaryPath, packagePath, true);
                progress?.Report(new UpdateDownloadProgress(
                    UpdateDownloadStage.Ready, requestUrl, Attempt: attempt, TotalAttempts: requestUrls.Count));
                return new PreparedUpdate(manifest.Version, packagePath, manifest.Sha256,
                    manifest.DownloadUrl, manifest.Signature);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryDelete(temporaryPath);
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException
                                               or InvalidDataException or TimeoutException)
            {
                lastError = exception;
                TryDelete(temporaryPath);
                progress?.Report(new UpdateDownloadProgress(
                    UpdateDownloadStage.RouteFailed, requestUrl, Attempt: attempt,
                    TotalAttempts: requestUrls.Count, Error: exception.Message));
            }
        }

        throw lastError ?? new HttpRequestException("所有更新下载地址均无法访问。");
    }

    internal static IReadOnlyList<string> BuildRequestUrls(string originalUrl, UpdateNetworkSettings? networkSettings)
    {
        var settings = (networkSettings ?? UpdateNetworkSettings.Default).Normalize();
        if (!Uri.TryCreate(originalUrl, UriKind.Absolute, out var originalUri)
            || !string.Equals(originalUri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return [originalUrl];

        var result = (settings.GithubProxies ?? [])
            .Select((proxy, index) => new { Proxy = proxy, Index = index })
            .Where(item => item.Proxy.Priority > 0)
            .OrderByDescending(item => item.Proxy.Priority)
            .ThenBy(item => item.Index)
            .Select(item => item.Proxy.IsDirect ? originalUrl : $"{item.Proxy.BaseUrl}/{originalUrl}")
            .ToList();
        return result;
    }

    internal static string ReadDistributionChannel(string baseDirectory)
    {
        try
        {
            var value = File.ReadAllText(Path.Combine(baseDirectory, DistributionChannelFileName)).Trim();
            return string.Equals(value, LiteDistributionChannel, StringComparison.OrdinalIgnoreCase)
                ? LiteDistributionChannel
                : FullDistributionChannel;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return FullDistributionChannel;
        }
    }

    internal static string GetManifestUrl(string baseDirectory)
    {
        var channel = ReadDistributionChannel(baseDirectory);
        return $"https://github.com/Kratosmax/gamepause/releases/latest/download/latest-{channel}.json";
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

    internal static async Task CopyDownloadAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        TimeSpan stallTimeout,
        CancellationToken cancellationToken = default,
        IProgress<UpdateDownloadProgress>? progress = null,
        string requestUrl = "",
        long? expectedBytes = null,
        int attempt = 1,
        int totalAttempts = 1)
    {
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (stallTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(stallTimeout));

        var buffer = new byte[81920];
        long totalBytes = 0;
        progress?.Report(new UpdateDownloadProgress(
            UpdateDownloadStage.Downloading, requestUrl, totalBytes, expectedBytes, attempt, totalAttempts));
        using var stallCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        while (true)
        {
            stallCancellation.CancelAfter(stallTimeout);
            int bytesRead;
            try
            {
                bytesRead = await source.ReadAsync(buffer, stallCancellation.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"更新下载连续 {stallTimeout.TotalSeconds:N0} 秒没有收到数据。");
            }
            finally
            {
                stallCancellation.CancelAfter(Timeout.InfiniteTimeSpan);
            }

            if (bytesRead == 0) break;
            totalBytes = checked(totalBytes + bytesRead);
            if (totalBytes > maxBytes)
                throw new InvalidDataException($"更新包超过 {maxBytes / 1024 / 1024} MB 安全上限。");
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            progress?.Report(new UpdateDownloadProgress(
                UpdateDownloadStage.Downloading, requestUrl, totalBytes, expectedBytes, attempt, totalAttempts));
        }
    }

    internal static bool CleanupDownloadedPackages(string? updatesRoot = null)
    {
        updatesRoot ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GamePause", "updates");
        try
        {
            if (Directory.Exists(updatesRoot)) Directory.Delete(updatesRoot, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static HttpClient CreateClient(UpdateNetworkSettings settings)
    {
        var handler = new HttpClientHandler();
        if (settings.HttpProxy is not null)
        {
            handler.Proxy = new WebProxy(settings.HttpProxy);
            handler.UseProxy = true;
        }
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GamePause", CurrentVersionText));
        return client;
    }

    private static bool IsStructurallyValid(UpdateManifest? manifest) =>
        manifest is not null
        && !string.IsNullOrWhiteSpace(manifest.Version)
        && !string.IsNullOrWhiteSpace(manifest.DownloadUrl)
        && !string.IsNullOrWhiteSpace(manifest.Sha256)
        && !string.IsNullOrWhiteSpace(manifest.Signature)
        && IsHttpUrl(manifest.DownloadUrl)
        && manifest.Sha256.Length == 64
        && manifest.Sha256.All(Uri.IsHexDigit);

    private static HttpRequestException CreateStatusException(string requestUrl, HttpStatusCode statusCode) =>
        new($"更新服务器返回 HTTP {(int)statusCode}：{requestUrl}", null, statusCode);

    private static bool IsHttpUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static void ValidatePackage(string path, string version)
    {
        using var archive = ZipFile.OpenRead(path);
        if (archive.GetEntry("GamePause.exe") is null
            || archive.GetEntry("GamePause.dll") is null
            || archive.GetEntry("GamePause.Updater.exe") is null)
            throw new InvalidDataException("更新包缺少主程序或更新器。");

        var channelEntry = archive.GetEntry(DistributionChannelFileName)
            ?? throw new InvalidDataException("更新包缺少发行通道标记。");
        using (var reader = new StreamReader(channelEntry.Open()))
        {
            var packageChannel = reader.ReadToEnd().Trim();
            if (!string.Equals(packageChannel, DistributionChannel, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"更新包通道 {packageChannel} 与当前 {DistributionChannel} 版本不一致。");
        }

        var packagedVersion = ReadAssemblyVersion(archive.GetEntry("GamePause.dll")!);
        if (!Version.TryParse(version.TrimStart('v', 'V'), out var expectedVersion)
            || !SameReleaseVersion(packagedVersion, expectedVersion))
            throw new InvalidDataException($"更新包版本 {packagedVersion} 与清单版本 {version} 不一致。");
    }

    private static Version ReadAssemblyVersion(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return PortableExecutableVersionReader.ReadAssemblyVersion(stream);
    }

    private static bool SameReleaseVersion(Version left, Version right) =>
        left.Major == right.Major && left.Minor == right.Minor && Math.Max(0, left.Build) == Math.Max(0, right.Build);

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
