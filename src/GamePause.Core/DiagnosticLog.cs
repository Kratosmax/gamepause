using System.Text;

namespace GamePause.Core;

public static class DiagnosticLog
{
    public const long DefaultMaxBytes = 5L * 1024 * 1024;
    private static readonly object SyncRoot = new();
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static bool Append(string path, string message, long maxBytes = DefaultMaxBytes)
    {
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        try
        {
            lock (SyncRoot)
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                var line = message + Environment.NewLine;
                var additionalBytes = Utf8NoBom.GetByteCount(line);
                if (File.Exists(path) && new FileInfo(path).Length + additionalBytes > maxBytes)
                {
                    File.Move(path, path + ".old", true);
                }
                File.AppendAllText(path, line, Utf8NoBom);
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
