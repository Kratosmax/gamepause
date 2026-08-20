namespace GamePause.Core;

public sealed record ProcessIdentity(
    int ProcessId,
    int ParentProcessId,
    int Depth,
    string Name,
    string? ExecutablePath,
    long StartTimeUtcTicks);

public sealed record WindowProcessInfo(
    int ProcessId,
    string Name,
    string WindowTitle,
    long WorkingSetBytes,
    bool IsProtected,
    string? ExecutablePath = null,
    long StartTimeUtcTicks = 0)
{
    public ProcessIdentity ToRootIdentity() => new(
        ProcessId, 0, 0, Name, ExecutablePath, StartTimeUtcTicks);
}

public sealed class ProcessIdentityMismatchException(string message) : InvalidOperationException(message);

public enum GamePauseMode
{
    Standard,
    Deep
}

public sealed record GameProfile(
    Guid Id,
    string DisplayName,
    string ProcessName,
    string? ExecutablePath,
    GamePauseMode PauseMode,
    bool AutoPauseEnabled,
    int FocusLossDelaySeconds,
    bool AutoResumeEnabled,
    bool AllowCautionAutomaticRules = false);

public enum CompatibilityRating
{
    Clear,
    Caution,
    Blocked
}

public sealed record CompatibilityAssessment(CompatibilityRating Rating, string Label, string Detail);

public enum SuspensionState
{
    Planned,
    Suspended,
    Failed,
    Resumed,
    Skipped,
    Suspending
}

public sealed record SessionProcess(
    ProcessIdentity Identity,
    SuspensionState State,
    string? Error = null,
    bool DeepTrimRequested = false,
    long? WorkingSetBeforeTrimBytes = null,
    long? WorkingSetAfterTrimBytes = null,
    string? TrimError = null);

public sealed record WorkingSetTrimResult(long BeforeBytes, long AfterBytes);

public sealed record SuspendedTarget(
    Guid TargetId,
    DateTimeOffset CreatedAt,
    string TargetName,
    int RootProcessId,
    IReadOnlyList<SessionProcess> Processes);

public sealed record SuspensionSession(
    int Version,
    IReadOnlyList<SuspendedTarget> Targets);

public sealed record OperationResult(bool Success, string Message, SuspensionSession? Session = null);

public sealed record KnownGame(string DisplayName, int SteamAppId, IReadOnlyList<string> ExpectedProcesses);
