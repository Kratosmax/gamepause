namespace GamePause.Core;

using System.Security.Cryptography;
using System.Text;

public static class GameProfileMatcher
{
    public static bool Matches(GameProfile profile, WindowProcessInfo process)
    {
        if (!string.IsNullOrWhiteSpace(profile.ExecutablePath) && !string.IsNullOrWhiteSpace(process.ExecutablePath))
        {
            return PathsEqual(profile.ExecutablePath, process.ExecutablePath);
        }

        return string.Equals(
            Path.GetFileNameWithoutExtension(profile.ProcessName),
            Path.GetFileNameWithoutExtension(process.Name),
            StringComparison.OrdinalIgnoreCase);
    }

    public static bool Matches(GameProfile profile, SuspendedTarget target)
    {
        var root = target.Processes.FirstOrDefault(process => process.Identity.ProcessId == target.RootProcessId)?.Identity;
        if (root is null) return false;
        if (!string.IsNullOrWhiteSpace(profile.ExecutablePath) && !string.IsNullOrWhiteSpace(root.ExecutablePath))
        {
            return PathsEqual(profile.ExecutablePath, root.ExecutablePath);
        }

        return string.Equals(
            Path.GetFileNameWithoutExtension(profile.ProcessName),
            Path.GetFileNameWithoutExtension(root.Name),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }
}

public static class CompatibilityChecker
{
    private static readonly HashSet<string> AntiCheatNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "easyanticheat", "easyanticheat_eos", "beservice", "battleye", "vgc", "vgtray",
        "faceit", "faceitclient", "mhyprot2", "mhyprotect", "ace-base", "s_guard64"
    };

    private static readonly HashSet<string> LauncherNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam", "steamwebhelper", "epicgameslauncher", "battle.net", "upc", "eadesktop",
        "start_protected_game", "launcher"
    };

    public static CompatibilityAssessment Assess(
        WindowProcessInfo process,
        IEnumerable<ProcessIdentity>? processTree = null,
        IEnumerable<string>? runningProcessNames = null)
    {
        if (process.IsProtected)
        {
            return new CompatibilityAssessment(CompatibilityRating.Blocked, "已阻止", "Windows 或 Game Pause 关键进程不可暂停。");
        }

        var normalizedName = Normalize(process.Name);
        var antiCheat = processTree?.FirstOrDefault(item => AntiCheatNames.Contains(Normalize(item.Name)));
        if (AntiCheatNames.Contains(normalizedName) || antiCheat is not null)
        {
            var component = antiCheat?.Name ?? process.Name;
            return new CompatibilityAssessment(CompatibilityRating.Blocked, "反作弊", $"进程树包含已知反作弊组件 {component}，为避免封禁或崩溃已阻止暂停。");
        }

        var globalAntiCheat = runningProcessNames?.FirstOrDefault(name => AntiCheatNames.Contains(Normalize(name)));
        if (globalAntiCheat is not null)
        {
            return new CompatibilityAssessment(CompatibilityRating.Caution, "检测到反作弊",
                $"系统中正在运行反作弊组件 {globalAntiCheat}；它可能属于其他游戏，请确认当前目标为安全的单机进程。");
        }

        if (LauncherNames.Contains(normalizedName) || normalizedName.Contains("launcher", StringComparison.OrdinalIgnoreCase))
        {
            return new CompatibilityAssessment(CompatibilityRating.Caution, "谨慎", "目标看起来是启动器；建议选择实际游戏进程。");
        }

        if (normalizedName.Contains("palworld", StringComparison.OrdinalIgnoreCase))
        {
            return new CompatibilityAssessment(CompatibilityRating.Caution, "仅限单机", "幻兽帕鲁仅建议在单人或本地主机模式暂停，联机服务器不会随客户端暂停。");
        }

        if (string.IsNullOrWhiteSpace(process.ExecutablePath))
        {
            return new CompatibilityAssessment(CompatibilityRating.Caution, "待确认", "无法读取程序路径；可手动暂停，但不会执行自动规则。");
        }

        return new CompatibilityAssessment(CompatibilityRating.Clear, "未见风险", "未发现已知反作弊、系统进程或启动器特征；这不代表兼容性保证。");
    }

    private static string Normalize(string name) => Path.GetFileNameWithoutExtension(name).Trim();
}

public sealed class AutoRuleTracker
{
    private readonly Dictionary<Guid, DateTimeOffset> _focusLostAt = [];

    public bool ShouldPause(GameProfile profile, bool isRunning, bool isForeground, bool isPaused, DateTimeOffset now)
    {
        if (!profile.AutoPauseEnabled || !isRunning || isPaused || isForeground)
        {
            _focusLostAt.Remove(profile.Id);
            return false;
        }

        if (!_focusLostAt.TryGetValue(profile.Id, out var lostAt))
        {
            _focusLostAt[profile.Id] = now;
            return false;
        }

        if ((now - lostAt).TotalSeconds < Math.Clamp(profile.FocusLossDelaySeconds, 3, 300)) return false;
        _focusLostAt.Remove(profile.Id);
        return true;
    }

    public void Reset(Guid profileId) => _focusLostAt.Remove(profileId);
}

public static class UpdateVersionPolicy
{
    public static bool IsNewer(string candidateVersion, Version currentVersion)
        => Version.TryParse(candidateVersion.TrimStart('v', 'V'), out var candidate)
           && candidate > currentVersion;
}

public static class UpdateManifestSecurity
{
    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAtnNgGPL6SbCcwDa4mSlm
        Wx2Opjga2jep80WEu5cELHPeG+l/YikbMgW0jmJRrSxg3XG0Ke1PFMXWYzBUEhTV
        jYd/n8rHzgxryXW5v/v4Lht6WHUV6iFtvk8OOKshKRldO4Djok1smCHtLo8vqU19
        ZXHMrFFfTt9RrosxR7EZwBmvxEGCqOXm4yg1dZalJpm/IZyNRt0/LiXWHBp0eV7f
        YTCrcQKRuNVBrkzqrbpG3gEl6jzES8u2sAqhj+o8rsoZelrE+zTlH5aRXq1dCUut
        ugV5YinsL7vrfG/9D81l+X0gL2Ur344xrU9sao5IR5ZIpKdITxpsCX3jqu+kBNdS
        7wIDAQAB
        -----END PUBLIC KEY-----
        """;

    public static bool Verify(string version, string downloadUrl, string sha256, string signature)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(PublicKeyPem);
            return rsa.VerifyData(
                BuildPayload(version, downloadUrl, sha256),
                Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or CryptographicException)
        {
            return false;
        }
    }

    public static byte[] BuildPayload(string version, string downloadUrl, string sha256) =>
        Encoding.UTF8.GetBytes($"{version.Trim()}\n{downloadUrl.Trim()}\n{sha256.Trim().ToUpperInvariant()}");
}
