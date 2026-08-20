using System.Text.Json;

namespace GamePause.App;

internal sealed record GithubProxySetting(string BaseUrl, int Priority, bool IsDirect = false);

internal sealed record UpdateNetworkSettings(
    List<GithubProxySetting>? GithubProxies = null,
    string? HttpProxy = null)
{
    internal static UpdateNetworkSettings Default { get; } = new([new GithubProxySetting(string.Empty, 10, true)], null);

    internal UpdateNetworkSettings Normalize()
    {
        var proxies = new List<GithubProxySetting>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasDirect = false;
        foreach (var proxy in GithubProxies ?? [])
        {
            if (proxy.IsDirect)
            {
                if (!hasDirect)
                {
                    proxies.Add(new GithubProxySetting(string.Empty, Math.Clamp(proxy.Priority, 0, 10), true));
                    hasDirect = true;
                }
                continue;
            }
            if (!TryNormalizeGithubProxy(proxy.BaseUrl, out var baseUrl) || !seen.Add(baseUrl)) continue;
            proxies.Add(new GithubProxySetting(baseUrl, Math.Clamp(proxy.Priority, 0, 10)));
        }
        if (!hasDirect)
        {
            proxies.Insert(0, new GithubProxySetting(string.Empty, proxies.Count == 0 ? 10 : 1, true));
        }

        return new UpdateNetworkSettings(
            proxies,
            TryNormalizeHttpProxy(HttpProxy, out var httpProxy) ? httpProxy : null);
    }

    internal static bool TryNormalizeGithubProxy(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (!TryCreateHttpUri(value, allowHttps: true, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)) return false;
        normalized = uri.AbsoluteUri.TrimEnd('/');
        return true;
    }

    internal static bool TryNormalizeHttpProxy(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!TryCreateHttpUri(value, allowHttps: false, out var uri)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)) return false;
        normalized = uri.GetLeftPart(UriPartial.Authority);
        return true;
    }

    private static bool TryCreateHttpUri(string? value, bool allowHttps, out Uri uri)
    {
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || allowHttps && parsed.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrWhiteSpace(parsed.Host))
        {
            uri = parsed;
            return true;
        }
        uri = null!;
        return false;
    }
}

internal sealed record UiSettings(
    bool CloseToTrayNoticeShown,
    string? SkippedUpdateVersion = null,
    UpdateNetworkSettings? UpdateNetwork = null,
    bool DebugModeEnabled = false)
{
    internal static UiSettings Default { get; } = new(false, null, UpdateNetworkSettings.Default);
    internal UpdateNetworkSettings EffectiveUpdateNetwork => (UpdateNetwork ?? UpdateNetworkSettings.Default).Normalize();
}

internal sealed class UiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    internal UiSettingsStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "ui-settings.json");
    }

    internal UiSettings Load()
    {
        try
        {
            var settings = File.Exists(_path)
                ? JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(_path), JsonOptions) ?? UiSettings.Default
                : UiSettings.Default;
            return settings with { UpdateNetwork = settings.EffectiveUpdateNetwork };
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return UiSettings.Default;
        }
    }

    internal bool Save(UiSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + ".tmp";
            settings = settings with { UpdateNetwork = settings.EffectiveUpdateNetwork };
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, _path, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed preference write must not prevent minimizing to the tray.
            return false;
        }
    }
}
