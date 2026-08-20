using System.Text.Json;
using GamePause.Core;

namespace GamePause.App;

internal sealed class GameProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly string _backupPath;

    internal GameProfileStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "profiles.json");
        _backupPath = _path + ".bak";
    }

    internal string? LastLoadError { get; private set; }
    internal bool IsWriteBlocked { get; private set; }

    internal IReadOnlyList<GameProfile> Load()
    {
        var candidates = new[] { _path, _path + ".tmp", _backupPath }
            .Where(File.Exists)
            .ToArray();
        if (candidates.Length == 0)
        {
            LastLoadError = null;
            IsWriteBlocked = false;
            return [];
        }

        var errors = new List<string>();
        foreach (var candidate in candidates)
        {
            try
            {
                var profiles = Deserialize(File.ReadAllText(candidate));
                IsWriteBlocked = false;
                LastLoadError = string.Equals(candidate, _path, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : $"游戏档案主文件无法读取，已从 {Path.GetFileName(candidate)} 恢复。";
                return profiles;
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
            {
                errors.Add($"{Path.GetFileName(candidate)}: {exception.Message}");
            }
        }

        IsWriteBlocked = true;
        LastLoadError = "游戏档案文件已损坏，已阻止覆盖：" + string.Join("；", errors);
        return [];
    }

    internal void Save(IEnumerable<GameProfile> profiles)
    {
        if (IsWriteBlocked)
            throw new InvalidDataException(LastLoadError ?? "游戏档案文件无法安全覆盖。");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var normalized = profiles.Select(Normalize)
            .OrderBy(profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var temporaryPath = _path + ".tmp";
        WriteThrough(temporaryPath, JsonSerializer.Serialize(normalized, JsonOptions));
        File.Move(temporaryPath, _path, true);
        File.Copy(_path, _backupPath, true);
        LastLoadError = null;
    }

    private static IReadOnlyList<GameProfile> Deserialize(string json)
    {
        var profiles = JsonSerializer.Deserialize<GameProfile[]>(json, JsonOptions)
                       ?? throw new InvalidDataException("档案内容为空。");
        if (profiles.Any(profile => profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.ProcessName)))
            throw new InvalidDataException("档案结构无效。");
        return profiles.Select(Normalize)
            .OrderBy(profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static void WriteThrough(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static GameProfile Normalize(GameProfile profile) => profile with
    {
        DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.ProcessName : profile.DisplayName.Trim(),
        ProcessName = Path.GetFileNameWithoutExtension(profile.ProcessName.Trim()),
        ExecutablePath = string.IsNullOrWhiteSpace(profile.ExecutablePath) ? null : profile.ExecutablePath.Trim(),
        FocusLossDelaySeconds = Math.Clamp(profile.FocusLossDelaySeconds, 3, 300)
    };
}
