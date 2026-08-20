using GamePause.Core;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

if (args.Contains("--memory-helper", StringComparer.Ordinal))
{
    var memory = GC.AllocateUninitializedArray<byte>(64 * 1024 * 1024);
    for (var index = 0; index < memory.Length; index += 4096)
    {
        memory[index] = 1;
    }
    Console.WriteLine("READY");
    Console.Out.Flush();
    Thread.Sleep(Timeout.Infinite);
    GC.KeepAlive(memory);
    return 0;
}

var tests = new (string Name, Action Run)[]
{
    ("Safety policy protects Windows and Game Pause processes", TestSafetyPolicy),
    ("Suspend orders children before parent and persists state", TestSuspendOrder),
    ("Suspend rejects a root identity whose PID was reused", TestStaleRootIdentity),
    ("Resume orders parent before children and clears state", TestResumeOrder),
    ("Recovery skips planned processes that were never submitted to Windows", TestPlannedProcessOwnership),
    ("Multiple targets can be suspended and selectively resumed", TestMultipleTargets),
    ("Deep suspend trims working sets and persists measurements", TestDeepSuspend),
    ("Deep trim failure keeps the process suspended and records the error", TestDeepTrimFailure),
    ("Windows working-set trim reduces memory for a disposable child process", TestNativeWorkingSetTrim),
    ("Native suspend verifies start time on the operation handle", TestNativeIdentityValidation),
    ("Reconcile removes processes that are no longer suspended", TestReconcile),
    ("Legacy single-target recovery state is migrated", TestLegacySessionMigration),
    ("Recovery state falls back to its backup when the primary file is corrupt", TestRecoveryBackupFallback),
    ("Corrupt recovery copies are reported instead of treated as an empty session", TestRecoveryCorruptionReported),
    ("PID reuse is skipped during resume", TestPidReuseProtection),
    ("Process search matches name, title, and PID", TestProcessSearch),
    ("Foreground filter returns only the foreground process", TestForegroundFilter),
    ("Game profiles prefer executable path and fall back to process name", TestGameProfileMatching),
    ("Compatibility checker blocks anti-cheat and cautions launchers", TestCompatibilityChecker),
    ("Automatic pause rule honors focus-loss delay", TestAutoRuleTracker),
    ("Update version policy accepts only newer semantic versions", TestUpdateVersionPolicy),
    ("Update manifests accept valid signatures and reject invalid signatures", TestUpdateManifestSignature),
    ("Compressed assembly versions are read from non-seekable ZIP streams", TestCompressedAssemblyVersionRead),
    ("Diagnostic logs rotate before exceeding their size limit", TestDiagnosticLogRotation)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void TestSafetyPolicy()
{
    var policy = new SafetyPolicy();
    Assert(policy.IsProtected(4, "anything"), "PID 4 must be protected.");
    Assert(policy.IsProtected(100, "lsass.exe"), "lsass must be protected.");
    Assert(policy.IsProtected(101, "GamePause.exe"), "the app must be protected.");
    Assert(!policy.IsProtected(4242, "sample-game.exe"), "a normal game should be allowed.");
}

static void TestSuspendOrder()
{
    using var fixture = new Fixture();
    var result = fixture.Service.SuspendTree(fixture.Root(100));
    Assert(result.Success, result.Message);
    AssertSequence(fixture.NativeApi.Suspended, [102, 101, 100]);
    var stored = fixture.Store.Load();
    Assert(stored is not null, "Recovery state must exist while paused.");
    Assert(stored!.Targets.Single().Processes.All(item => item.State == SuspensionState.Suspended), "All states should be suspended.");
}

static void TestStaleRootIdentity()
{
    using var fixture = new Fixture();
    var stale = fixture.Root(100) with { StartTimeUtcTicks = 999 };
    var result = fixture.Service.SuspendTree(stale);
    Assert(!result.Success, "A stale selected process identity must be rejected.");
    Assert(fixture.NativeApi.Suspended.Count == 0, "No process may be suspended after PID reuse is detected.");
}

static void TestMultipleTargets()
{
    using var fixture = new Fixture();
    var paused = fixture.Service.SuspendTrees([fixture.Root(100), fixture.Root(200)]);
    Assert(paused.Success, paused.Message);
    Assert(paused.Session?.Targets.Count == 2, "Two paused targets should be recorded.");
    AssertSequence(fixture.NativeApi.Suspended, [102, 101, 100, 201, 200]);

    var firstTargetId = paused.Session!.Targets.Single(target => target.RootProcessId == 100).TargetId;
    var resumed = fixture.Service.ResumeTargets([firstTargetId]);
    Assert(resumed.Success, resumed.Message);
    AssertSequence(fixture.NativeApi.Resumed, [100, 101, 102]);
    Assert(fixture.Store.Load()?.Targets.Single().RootProcessId == 200, "The unselected target must remain paused.");
}

static void TestDeepSuspend()
{
    using var fixture = new Fixture();
    var result = fixture.Service.SuspendTrees([fixture.Root(100)], trimWorkingSets: true);
    Assert(result.Success, result.Message);
    AssertSequence(fixture.NativeApi.Trimmed, [102, 101, 100]);
    var processes = fixture.Store.Load()!.Targets.Single().Processes;
    Assert(processes.All(process => process.DeepTrimRequested), "Deep trim intent must be persisted.");
    Assert(processes.All(process => process.WorkingSetBeforeTrimBytes == 4096), "Before working set must be stored.");
    Assert(processes.All(process => process.WorkingSetAfterTrimBytes == 1024), "After working set must be stored.");
}

static void TestDeepTrimFailure()
{
    using var fixture = new Fixture();
    fixture.NativeApi.TrimFailures.Add(101);
    var result = fixture.Service.SuspendTrees([fixture.Root(100)], trimWorkingSets: true);
    Assert(result.Success, result.Message);
    var failedTrim = fixture.Store.Load()!.Targets.Single().Processes.Single(process => process.Identity.ProcessId == 101);
    Assert(failedTrim.State == SuspensionState.Suspended, "A trim failure must not change suspension state.");
    Assert(!string.IsNullOrWhiteSpace(failedTrim.TrimError), "A trim failure must be persisted.");
}

static void TestNativeWorkingSetTrim()
{
    var executablePath = Environment.ProcessPath
        ?? throw new InvalidOperationException("Unable to locate the test executable.");
    using var process = Process.Start(new ProcessStartInfo(executablePath, "--memory-helper")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        CreateNoWindow = true
    }) ?? throw new InvalidOperationException("Unable to start the memory probe.");
    var nativeApi = new NativeProcessApi();
    var suspended = false;
    try
    {
        Assert(process.StandardOutput.ReadLine() == "READY", "Memory probe did not become ready.");
        var identity = new ProcessIdentity(
            process.Id, 0, 0, process.ProcessName, null, process.StartTime.ToUniversalTime().Ticks);
        nativeApi.Suspend(identity);
        suspended = true;
        var result = nativeApi.TrimWorkingSet(process.Id);
        Assert(result.BeforeBytes >= 32L * 1024 * 1024, "Memory probe did not commit the expected working set.");
        Assert(result.AfterBytes < result.BeforeBytes, "Windows did not reduce the child working set.");
        nativeApi.Resume(identity);
        suspended = false;
    }
    finally
    {
        if (suspended)
        {
            try
            {
                nativeApi.Resume(new ProcessIdentity(
                    process.Id, 0, 0, process.ProcessName, null, process.StartTime.ToUniversalTime().Ticks));
            }
            catch { }
        }
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
    }
}

static void TestNativeIdentityValidation()
{
    var executablePath = Environment.ProcessPath
        ?? throw new InvalidOperationException("Unable to locate the test executable.");
    using var process = Process.Start(new ProcessStartInfo(executablePath, "--memory-helper")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        CreateNoWindow = true
    }) ?? throw new InvalidOperationException("Unable to start the identity probe.");
    try
    {
        Assert(process.StandardOutput.ReadLine() == "READY", "Identity probe did not become ready.");
        var stale = new ProcessIdentity(
            process.Id, 0, 0, process.ProcessName, null, process.StartTime.ToUniversalTime().Ticks + 1);
        AssertThrows<ProcessIdentityMismatchException>(() => new NativeProcessApi().Suspend(stale),
            "A mismatched process start time must be rejected before suspension.");
    }
    finally
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
    }
}

static void TestReconcile()
{
    using var fixture = new Fixture();
    fixture.Service.SuspendTree(fixture.Root(100));
    fixture.NativeApi.SuspendedProcessIds.Remove(101);
    var result = fixture.Service.ReconcileActiveSession();
    Assert(result.Success, result.Message);
    var processes = fixture.Store.Load()!.Targets.Single().Processes;
    Assert(processes.Single(item => item.Identity.ProcessId == 101).State == SuspensionState.Resumed,
        "A process that is no longer suspended should be marked resumed.");
}

static void TestLegacySessionMigration()
{
    using var fixture = new Fixture();
    var legacyJson = """
        {
          "SessionId": "11111111-1111-1111-1111-111111111111",
          "CreatedAt": "2026-08-19T00:00:00+08:00",
          "TargetName": "Legacy Game",
          "RootProcessId": 100,
          "Processes": []
        }
        """;
    Directory.CreateDirectory(fixture.Store.DataDirectory);
    File.WriteAllText(fixture.Store.SessionFilePath, legacyJson);
    var migrated = fixture.Store.Load();
    Assert(migrated?.Version == 2, "Legacy state should migrate to version 2.");
    Assert(migrated?.Targets.Single().TargetName == "Legacy Game", "Legacy target should be retained.");
}

static void TestRecoveryBackupFallback()
{
    using var fixture = new Fixture();
    Assert(fixture.Service.SuspendTree(fixture.Root(100)).Success, "Fixture pause failed.");
    Assert(File.Exists(fixture.Store.BackupFilePath), "A recovery backup must be written.");
    File.WriteAllText(fixture.Store.SessionFilePath, "{invalid json");
    var recovered = fixture.Store.Load();
    Assert(recovered?.Targets.Count == 1, "The valid backup should be loaded.");
    Assert(fixture.Store.LastLoadError is null, "A successful fallback must clear the load error.");
}

static void TestRecoveryCorruptionReported()
{
    using var fixture = new Fixture();
    Directory.CreateDirectory(fixture.Store.DataDirectory);
    File.WriteAllText(fixture.Store.SessionFilePath, "broken");
    File.WriteAllText(fixture.Store.SessionFilePath + ".tmp", "broken");
    File.WriteAllText(fixture.Store.BackupFilePath, "broken");
    Assert(fixture.Store.Load() is null, "Corrupt recovery files cannot produce a session.");
    Assert(!string.IsNullOrWhiteSpace(fixture.Store.LastLoadError), "Corruption must remain visible to callers.");
    var resume = fixture.Service.ResumeActiveSession();
    Assert(!resume.Success && resume.Message.Contains("损坏", StringComparison.Ordinal),
        "Emergency resume must report corrupt recovery state.");
}

static void TestResumeOrder()
{
    using var fixture = new Fixture();
    fixture.Service.SuspendTree(fixture.Root(100));
    var result = fixture.Service.ResumeActiveSession();
    Assert(result.Success, result.Message);
    AssertSequence(fixture.NativeApi.Resumed, [100, 101, 102]);
    Assert(fixture.Store.Load() is null, "Recovery state must be cleared after a successful resume.");
}

static void TestPlannedProcessOwnership()
{
    using var fixture = new Fixture();
    var identity = new ProcessIdentity(100, 0, 0, "sample-game.exe", "C:\\Games\\sample-game.exe", 1000);
    fixture.Store.Save(new SuspensionSession(2,
        [new SuspendedTarget(Guid.NewGuid(), DateTimeOffset.Now, "Sample", 100,
            [new SessionProcess(identity, SuspensionState.Planned)])]));
    var result = fixture.Service.ResumeActiveSession();
    Assert(result.Success, result.Message);
    Assert(fixture.NativeApi.Resumed.Count == 0, "A merely planned process must not receive NtResumeProcess.");
    Assert(fixture.Store.Load() is null, "A never-attempted target should be cleared from recovery state.");
}

static void TestPidReuseProtection()
{
    using var fixture = new Fixture();
    fixture.Service.SuspendTree(fixture.Root(100));
    fixture.Catalog.InvalidProcessIds.Add(101);
    var result = fixture.Service.ResumeActiveSession();
    Assert(result.Success, result.Message);
    AssertSequence(fixture.NativeApi.Resumed, [100, 102]);
}

static void TestProcessSearch()
{
    var processes = SampleWindowProcesses();
    Assert(ProcessListFilter.Apply(processes, "Palworld", false, 202).Single().ProcessId == 202, "Name search failed.");
    Assert(ProcessListFilter.Apply(processes, "黑神话", false, 202).Single().ProcessId == 303, "Window title search failed.");
    Assert(ProcessListFilter.Apply(processes, "101", false, 202).Single().ProcessId == 101, "PID search failed.");
}

static void TestForegroundFilter()
{
    var result = ProcessListFilter.Apply(SampleWindowProcesses(), null, true, 202);
    Assert(result.Count == 1, "Foreground-only filter should return one process.");
    Assert(result[0].ProcessId == 202, "Foreground process should be returned.");

    var ordered = ProcessListFilter.Apply(SampleWindowProcesses(), null, false, 202);
    Assert(ordered[0].ProcessId == 202, "Foreground process should be listed first.");
}

static void TestGameProfileMatching()
{
    var profile = new GameProfile(Guid.NewGuid(), "Sample", "sample-game", "C:\\Games\\sample-game.exe",
        GamePauseMode.Standard, false, 10, false);
    Assert(GameProfileMatcher.Matches(profile,
        new WindowProcessInfo(1, "renamed", "Sample", 1024, false, "C:\\Games\\sample-game.exe")),
        "Exact executable path should match even when the process name differs.");
    Assert(!GameProfileMatcher.Matches(profile,
        new WindowProcessInfo(2, "sample-game", "Other", 1024, false, "D:\\Other\\sample-game.exe")),
        "A different readable path must not fall back to the process name.");

    var nameOnly = profile with { ExecutablePath = null };
    Assert(GameProfileMatcher.Matches(nameOnly,
        new WindowProcessInfo(3, "sample-game.exe", "Sample", 1024, false)),
        "A profile without a path should match by normalized process name.");
}

static void TestCompatibilityChecker()
{
    var normal = new WindowProcessInfo(10, "sample-game", "Sample", 1024, false, "C:\\Games\\sample-game.exe");
    Assert(CompatibilityChecker.Assess(normal).Rating == CompatibilityRating.Clear, "A normal executable should be clear.");
    Assert(CompatibilityChecker.Assess(normal,
        [new ProcessIdentity(11, 10, 1, "EasyAntiCheat_EOS.exe", null, 1)]).Rating == CompatibilityRating.Blocked,
        "A known anti-cheat child must block suspension.");
    Assert(CompatibilityChecker.Assess(normal, [], ["BEService"]).Rating == CompatibilityRating.Caution,
        "A system-wide anti-cheat service should require confirmation rather than block an unrelated target.");
    Assert(CompatibilityChecker.Assess(
        new WindowProcessInfo(12, "EpicGamesLauncher", "Epic", 1024, false, "C:\\Epic\\Launcher.exe")).Rating
        == CompatibilityRating.Caution, "A launcher should require confirmation.");
}

static void TestAutoRuleTracker()
{
    var tracker = new AutoRuleTracker();
    var profile = new GameProfile(Guid.NewGuid(), "Sample", "sample-game", "C:\\Games\\sample-game.exe",
        GamePauseMode.Standard, true, 10, true);
    var start = DateTimeOffset.Parse("2026-08-20T10:00:00+08:00");
    Assert(!tracker.ShouldPause(profile, true, false, false, start), "The first focus loss observation should start the timer.");
    Assert(!tracker.ShouldPause(profile, true, false, false, start.AddSeconds(9)), "The rule must wait for its delay.");
    Assert(tracker.ShouldPause(profile, true, false, false, start.AddSeconds(10)), "The rule should fire when the delay elapses.");
    Assert(!tracker.ShouldPause(profile, true, true, false, start.AddSeconds(20)), "Foreground use must reset the rule.");
}

static void TestUpdateVersionPolicy()
{
    var current = new Version(0, 9, 0);
    Assert(UpdateVersionPolicy.IsNewer("0.9.1", current), "A newer patch version should be offered.");
    Assert(UpdateVersionPolicy.IsNewer("v1.0.0", current), "A v-prefixed major version should be offered.");
    Assert(!UpdateVersionPolicy.IsNewer("0.9.0", current), "The current version must not be offered.");
    Assert(!UpdateVersionPolicy.IsNewer("invalid", current), "An invalid version must be ignored.");
}

static void TestUpdateManifestSignature()
{
    const string downloadUrl =
        "https://github.com/Kratosmax/gamepause/releases/download/v1.0.0/GamePause-1.0.0.zip";
    const string validSignature =
        "dWqe7A0DWMUPSdjocUNA4sHiuChA/iocu38HbThyvCTQyWzaUuxrZG+zD9PzTALa8cW4mnqD6KFpXcDG0K8Qigf662iNDu+7yRJn+N6sjAKlb/K4dROnrAo+6GyMdz5PMC7tHZfM6JsY9TDshSSPvrFmyX3N7cJtxXA+HB3Rkp2ZJsKYkpZPDjO85ZNxcFDSUgc3Rl/Rk/FSNkYnPIH/IauVMVwsbmv1SFoztMKaVXTZ4n4F7DJAGKF7Ha3kxTj/JHE3bhxkNAAHvbDybynqo6H9u1Pl9H2P7S6QdV2FytcWSLGs/k4LGJ6qdBen8g0Bje/HTtYTQgSU9di4wU5Fqb0EMIG6rWnX4qxzF8yFqjirzePHlhaHmKK40DioEkdd0UDiGgBd33FznaTcOsmCF81VQWkh6pFZPv7eDVLESDCfvPGA7SqpEo1ZR8hVDMVD6JL5Mc59pA1FTw2cH4EyP+rsyYkf9HVEEq5CaR7U39/eGU8el+ifMl4DCqABjViH";
    Assert(UpdateManifestSecurity.Verify("1.0.0", downloadUrl, new string('A', 64), validSignature),
        "A manifest signed by the release key should be accepted.");
    Assert(!UpdateManifestSecurity.Verify(
            "1.0.0", "https://example.com/GamePause.zip", new string('A', 64), Convert.ToBase64String(new byte[256])),
        "An invalid update signature must be rejected.");
    Assert(!UpdateManifestSecurity.Verify("1.0.0", "https://example.com/GamePause.zip", new string('A', 64), "not-base64"),
        "Malformed signature data must be rejected.");
}

static void TestCompressedAssemblyVersionRead()
{
    using var package = new MemoryStream();
    using (var archive = new ZipArchive(package, ZipArchiveMode.Create, leaveOpen: true))
    {
        var entry = archive.CreateEntry("GamePause.dll", CompressionLevel.Optimal);
        using var source = File.OpenRead(Assembly.GetExecutingAssembly().Location);
        using var destination = entry.Open();
        source.CopyTo(destination);
    }

    package.Position = 0;
    using var readArchive = new ZipArchive(package, ZipArchiveMode.Read);
    using var compressedStream = readArchive.GetEntry("GamePause.dll")!.Open();
    Assert(!compressedStream.CanSeek, "The regression fixture must expose a non-seekable ZIP entry stream.");
    var actual = PortableExecutableVersionReader.ReadAssemblyVersion(compressedStream);
    Assert(actual == Assembly.GetExecutingAssembly().GetName().Version,
        $"Expected the packaged assembly version, got {actual}.");

    using var oversized = new MemoryStream(new byte[33]);
    AssertThrows<InvalidDataException>(
        () => PortableExecutableVersionReader.ReadAssemblyVersion(oversized, 32),
        "The PE buffering safety limit must be enforced.");
}

static void TestDiagnosticLogRotation()
{
    var directory = Path.Combine(Path.GetTempPath(), "GamePauseLogTests", Guid.NewGuid().ToString("N"));
    var path = Path.Combine(directory, "test.log");
    try
    {
        Assert(DiagnosticLog.Append(path, new string('A', 20), 32), "First log write failed.");
        Assert(DiagnosticLog.Append(path, new string('B', 20), 32), "Second log write failed.");
        Assert(File.Exists(path + ".old"), "The previous log must be retained after rotation.");
        Assert(File.ReadAllText(path + ".old").Contains('A'), "The rotated log lost its previous content.");
        Assert(File.ReadAllText(path).Contains('B'), "The active log does not contain the latest entry.");
    }
    finally
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}

static WindowProcessInfo[] SampleWindowProcesses() =>
[
    new(101, "notepad", "notes.txt", 1024, false),
    new(202, "Palworld-Win64-Shipping", "Palworld", 2048, false),
    new(303, "b1-Win64-Shipping", "黑神话：悟空", 4096, false)
];

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertThrows<TException>(Action action, string message) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }
    throw new InvalidOperationException(message);
}

static void AssertSequence(IReadOnlyList<int> actual, IReadOnlyList<int> expected)
{
    Assert(actual.SequenceEqual(expected), $"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}].");
}

sealed class Fixture : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GamePauseTests", Guid.NewGuid().ToString("N"));

    public Fixture()
    {
        Catalog = new FakeCatalog();
        NativeApi = new FakeNativeApi();
        Store = new SessionStore(_directory);
        Service = new ProcessSuspensionService(Catalog, NativeApi, new SafetyPolicy(), Store);
    }

    public FakeCatalog Catalog { get; }
    public FakeNativeApi NativeApi { get; }
    public SessionStore Store { get; }
    public ProcessSuspensionService Service { get; }

    public ProcessIdentity Root(int processId) => Catalog.GetProcessTree(processId)
        .Single(process => process.ProcessId == processId);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}

sealed class FakeCatalog : IProcessCatalog
{
    private readonly ProcessIdentity[] _tree =
    [
        new(100, 0, 0, "sample-game.exe", "C:\\Games\\sample-game.exe", 1000),
        new(101, 100, 1, "renderer.exe", "C:\\Games\\renderer.exe", 1001),
        new(102, 101, 2, "worker.exe", "C:\\Games\\worker.exe", 1002)
    ];

    private readonly ProcessIdentity[] _secondTree =
    [
        new(200, 0, 0, "second-game.exe", "C:\\Games\\second-game.exe", 2000),
        new(201, 200, 1, "second-worker.exe", "C:\\Games\\second-worker.exe", 2001)
    ];

    public HashSet<int> InvalidProcessIds { get; } = [];

    public ProcessIdentity? GetForegroundProcess() => _tree[0];
    public IReadOnlyList<ProcessIdentity> GetProcessTree(int rootProcessId) => rootProcessId == 200 ? _secondTree : _tree;
    public IReadOnlyList<WindowProcessInfo> GetWindowProcesses() => [];
    public IReadOnlyList<string> GetRunningProcessNames() => [];
    public bool IsSameProcess(ProcessIdentity identity) => !InvalidProcessIds.Contains(identity.ProcessId);
}

sealed class FakeNativeApi : IProcessNativeApi
{
    public List<int> Suspended { get; } = [];
    public List<int> Resumed { get; } = [];
    public HashSet<int> SuspendedProcessIds { get; } = [];
    public List<int> Trimmed { get; } = [];
    public HashSet<int> TrimFailures { get; } = [];

    public void Suspend(ProcessIdentity identity)
    {
        var processId = identity.ProcessId;
        Suspended.Add(processId);
        SuspendedProcessIds.Add(processId);
    }

    public void Resume(ProcessIdentity identity)
    {
        var processId = identity.ProcessId;
        Resumed.Add(processId);
        SuspendedProcessIds.Remove(processId);
    }

    public bool IsSuspended(int processId) => SuspendedProcessIds.Contains(processId);

    public WorkingSetTrimResult TrimWorkingSet(int processId)
    {
        if (TrimFailures.Contains(processId))
        {
            throw new InvalidOperationException("Simulated trim failure.");
        }
        Trimmed.Add(processId);
        return new WorkingSetTrimResult(4096, 1024);
    }
}
