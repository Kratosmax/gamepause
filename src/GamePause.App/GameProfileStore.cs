using System.Text.Json;
using GamePause.Core;

namespace GamePause.App;

internal sealed class GameProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    internal GameProfileStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "profiles.json");
    }

    internal IReadOnlyList<GameProfile> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            return (JsonSerializer.Deserialize<GameProfile[]>(File.ReadAllText(_path), JsonOptions) ?? [])
                .Where(profile => profile.Id != Guid.Empty && !string.IsNullOrWhiteSpace(profile.ProcessName))
                .Select(Normalize)
                .OrderBy(profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    internal void Save(IEnumerable<GameProfile> profiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var normalized = profiles.Select(Normalize)
            .OrderBy(profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(normalized, JsonOptions));
        File.Move(temporaryPath, _path, true);
    }

    private static GameProfile Normalize(GameProfile profile) => profile with
    {
        DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.ProcessName : profile.DisplayName.Trim(),
        ProcessName = Path.GetFileNameWithoutExtension(profile.ProcessName.Trim()),
        ExecutablePath = string.IsNullOrWhiteSpace(profile.ExecutablePath) ? null : profile.ExecutablePath.Trim(),
        FocusLossDelaySeconds = Math.Clamp(profile.FocusLossDelaySeconds, 3, 300)
    };
}
