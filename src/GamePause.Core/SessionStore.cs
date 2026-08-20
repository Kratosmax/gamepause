using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace GamePause.Core;

public sealed class SessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SessionStore(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GamePause");
        SessionFilePath = Path.Combine(DataDirectory, "active-session.json");
        BackupFilePath = SessionFilePath + ".bak";
        LogFilePath = Path.Combine(DataDirectory, "game-pause.log");
    }

    public string DataDirectory { get; }
    public string SessionFilePath { get; }
    public string BackupFilePath { get; }
    public string LogFilePath { get; }
    public string? LastLoadError { get; private set; }
    public bool DebugLoggingEnabled { get; set; }

    internal IDisposable AcquireOperationLock()
    {
        var normalizedDirectory = Path.GetFullPath(DataDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedDirectory)));
        var mutex = new Mutex(false, $@"Local\GamePause.Session.{hash}");
        try
        {
            try
            {
                mutex.WaitOne();
            }
            catch (AbandonedMutexException)
            {
                Log("Recovered an abandoned pause-operation lock.");
            }
            return new OperationLock(mutex);
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    public SuspensionSession? Load()
    {
        var candidates = new[] { SessionFilePath, SessionFilePath + ".tmp", BackupFilePath }
            .Where(File.Exists)
            .ToArray();
        if (candidates.Length == 0)
        {
            LastLoadError = null;
            return null;
        }

        var errors = new List<string>();
        foreach (var path in candidates)
        {
            try
            {
                var session = Deserialize(File.ReadAllText(path));
                LastLoadError = null;
                if (!string.Equals(path, SessionFilePath, StringComparison.OrdinalIgnoreCase))
                    Log($"Recovery state loaded from fallback file {Path.GetFileName(path)}.");
                return session.Targets.Count > 0 ? session : null;
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or InvalidDataException)
            {
                errors.Add($"{Path.GetFileName(path)}: {exception.Message}");
            }
        }

        LastLoadError = "暂停恢复记录已损坏：" + string.Join("；", errors);
        Log(LastLoadError);
        return null;
    }

    public void Save(SuspensionSession session)
    {
        Directory.CreateDirectory(DataDirectory);
        var temporaryPath = SessionFilePath + ".tmp";
        WriteThrough(temporaryPath, JsonSerializer.Serialize(session, JsonOptions));
        if (File.Exists(SessionFilePath))
        {
            File.Replace(temporaryPath, SessionFilePath, BackupFilePath, true);
        }
        else
        {
            File.Move(temporaryPath, SessionFilePath);
            File.Copy(SessionFilePath, BackupFilePath, true);
        }
        LastLoadError = null;
    }

    public void Clear()
    {
        try
        {
            File.Delete(SessionFilePath);
            File.Delete(SessionFilePath + ".tmp");
            File.Delete(BackupFilePath);
            LastLoadError = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log($"Unable to clear recovery state: {exception.Message}");
        }
    }

    private static SuspensionSession Deserialize(string json)
    {
        using var document = JsonDocument.Parse(json);
        SuspensionSession session;
        if (document.RootElement.TryGetProperty("Targets", out _))
        {
            session = JsonSerializer.Deserialize<SuspensionSession>(json, JsonOptions)
                ?? throw new InvalidDataException("记录内容为空。");
        }
        else
        {
            var legacy = JsonSerializer.Deserialize<LegacySuspensionSession>(json, JsonOptions)
                ?? throw new InvalidDataException("旧版记录内容为空。");
            session = new SuspensionSession(2,
                [new SuspendedTarget(legacy.SessionId, legacy.CreatedAt, legacy.TargetName,
                    legacy.RootProcessId, legacy.Processes)]);
        }
        Validate(session);
        return session;
    }

    private static void Validate(SuspensionSession session)
    {
        if (session.Version is < 1 or > 2 || session.Targets is null)
            throw new InvalidDataException("记录版本或目标集合无效。");
        if (session.Targets.Any(target => target is null
                || target.TargetId == Guid.Empty
                || target.RootProcessId <= 0
                || string.IsNullOrWhiteSpace(target.TargetName)
                || target.Processes is null))
            throw new InvalidDataException("暂停目标结构无效。");
        if (session.Targets.Select(target => target.TargetId).Distinct().Count() != session.Targets.Count)
            throw new InvalidDataException("暂停目标标识重复。");
        if (session.Targets.SelectMany(target => target.Processes).Any(process => process is null
                || process.Identity is null
                || process.Identity.ProcessId <= 0
                || process.Identity.StartTimeUtcTicks <= 0
                || string.IsNullOrWhiteSpace(process.Identity.Name)))
            throw new InvalidDataException("进程身份结构无效。");
    }

    private static void WriteThrough(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    public void Log(string message)
    {
        DiagnosticLog.Append(
            LogFilePath,
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}");
    }

    public void LogProcessTree(string stage, ProcessIdentity root, IEnumerable<ProcessIdentity> processes)
    {
        if (!DebugLoggingEnabled) return;
        var snapshot = processes
            .OrderBy(process => process.Depth)
            .ThenBy(process => process.ProcessId)
            .ToArray();
        Log($"{stage} process tree for {root.Name} ({root.ProcessId}); {snapshot.Length} process(es).");
        foreach (var process in snapshot)
        {
            Log($"Process tree item: depth={process.Depth}; pid={process.ProcessId}; parent={process.ParentProcessId}; "
                + $"name={process.Name}; path={process.ExecutablePath ?? "unavailable"}");
        }
    }

    public void LogDebug(string message)
    {
        if (DebugLoggingEnabled) Log(message);
    }

    private sealed record LegacySuspensionSession(
        Guid SessionId,
        DateTimeOffset CreatedAt,
        string TargetName,
        int RootProcessId,
        IReadOnlyList<SessionProcess> Processes);

    private sealed class OperationLock(Mutex mutex) : IDisposable
    {
        private Mutex? _mutex = mutex;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _mutex, null);
            if (current is null) return;
            current.ReleaseMutex();
            current.Dispose();
        }
    }
}
