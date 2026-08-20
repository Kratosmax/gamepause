using System.Text.Json;

namespace GamePause.App;

internal sealed record HotkeyGesture(uint Modifiers, Keys Key)
{
    internal string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if ((Modifiers & GlobalHotkeys.Control) != 0) parts.Add("Ctrl");
            if ((Modifiers & GlobalHotkeys.Alt) != 0) parts.Add("Alt");
            if ((Modifiers & GlobalHotkeys.Shift) != 0) parts.Add("Shift");
            parts.Add(Key.ToString());
            return string.Join(" + ", parts);
        }
    }
}

internal sealed record HotkeySettings(HotkeyGesture Toggle, HotkeyGesture Emergency)
{
    internal static HotkeySettings Default { get; } = new(
        new HotkeyGesture(GlobalHotkeys.Control | GlobalHotkeys.Alt, Keys.F12),
        new HotkeyGesture(GlobalHotkeys.Control | GlobalHotkeys.Alt | GlobalHotkeys.Shift, Keys.F12));
}

internal sealed class HotkeySettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;

    internal HotkeySettingsStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "settings.json");
    }

    internal HotkeySettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return HotkeySettings.Default;
            }

            var settings = JsonSerializer.Deserialize<HotkeySettings>(File.ReadAllText(_path), JsonOptions);
            return settings is { Toggle: not null, Emergency: not null }
                ? settings
                : HotkeySettings.Default;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return HotkeySettings.Default;
        }
    }

    internal void Save(HotkeySettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, _path, true);
    }
}
