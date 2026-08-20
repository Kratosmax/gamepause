using System.Text.Json;

namespace GamePause.App;

internal sealed record UiSettings(bool CloseToTrayNoticeShown, string? SkippedUpdateVersion = null)
{
    internal static UiSettings Default { get; } = new(false, null);
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
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(_path), JsonOptions) ?? UiSettings.Default
                : UiSettings.Default;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return UiSettings.Default;
        }
    }

    internal void Save(UiSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporaryPath = _path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, _path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed preference write must not prevent minimizing to the tray.
        }
    }
}
