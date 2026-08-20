namespace GamePause.Core;

public sealed class ProcessSuspensionService
{
    private readonly IProcessCatalog _catalog;
    private readonly IProcessNativeApi _nativeApi;
    private readonly SafetyPolicy _safetyPolicy;
    private readonly SessionStore _store;
    private readonly object _syncRoot = new();

    public ProcessSuspensionService(
        IProcessCatalog catalog,
        IProcessNativeApi nativeApi,
        SafetyPolicy safetyPolicy,
        SessionStore store)
    {
        _catalog = catalog;
        _nativeApi = nativeApi;
        _safetyPolicy = safetyPolicy;
        _store = store;
    }

    public SuspensionSession? ActiveSession => _store.Load();

    public OperationResult SuspendTree(ProcessIdentity rootProcess) => SuspendTrees([rootProcess]);

    public OperationResult SuspendTrees(IEnumerable<ProcessIdentity> rootProcesses, bool trimWorkingSets = false)
    {
        lock (_syncRoot)
        {
            var existing = _store.Load();
            if (existing is null && _store.LastLoadError is not null)
                return new OperationResult(false, _store.LastLoadError);
            var targets = existing?.Targets.ToList() ?? [];
            var claimedProcessIds = targets
                .SelectMany(target => target.Processes)
                .Where(process => process.State is SuspensionState.Planned or SuspensionState.Suspending or SuspensionState.Suspended)
                .Select(process => process.Identity.ProcessId)
                .ToHashSet();
            var newTargets = new List<SuspendedTarget>();

            foreach (var requestedRoot in rootProcesses
                         .GroupBy(process => process.ProcessId)
                         .Select(group => group.First()))
            {
                var rootProcessId = requestedRoot.ProcessId;
                var tree = _catalog.GetProcessTree(rootProcessId);
                var currentRoot = tree.FirstOrDefault(process => process.ProcessId == rootProcessId);
                if (currentRoot is null || currentRoot.StartTimeUtcTicks != requestedRoot.StartTimeUtcTicks)
                {
                    continue;
                }
                var safeProcesses = tree
                    .Where(process => !_safetyPolicy.IsProtected(process))
                    .Where(process => !claimedProcessIds.Contains(process.ProcessId))
                    .OrderByDescending(process => process.Depth)
                    .ThenByDescending(process => process.ProcessId)
                    .ToArray();

                if (safeProcesses.All(process => process.ProcessId != rootProcessId))
                {
                    continue;
                }

                var root = safeProcesses.First(process => process.ProcessId == rootProcessId);
                var target = new SuspendedTarget(
                    Guid.NewGuid(),
                    DateTimeOffset.Now,
                    KnownGames.GetDisplayName(root.Name),
                    rootProcessId,
                    safeProcesses.Select(process => new SessionProcess(
                        process,
                        SuspensionState.Planned,
                        DeepTrimRequested: trimWorkingSets)).ToArray());
                newTargets.Add(target);
                foreach (var process in safeProcesses)
                {
                    claimedProcessIds.Add(process.ProcessId);
                }
            }

            if (newTargets.Count == 0)
            {
                return new OperationResult(false, "没有可暂停的新进程；它们可能已暂停、已退出或受保护。", existing);
            }

            targets.AddRange(newTargets);
            var session = new SuspensionSession(2, targets.ToArray());
            _store.Save(session);

            var suspendedTargets = 0;
            var suspendedProcesses = 0;
            var trimmedProcesses = 0;
            foreach (var target in newTargets)
            {
                var currentTarget = target;
                _store.Log($"Pause requested for {target.TargetName} ({target.RootProcessId}); {target.Processes.Count} process(es).");
                var states = target.Processes.ToArray();
                var targetSuspendedCount = 0;
                for (var index = 0; index < states.Length; index++)
                {
                    states[index] = states[index] with { State = SuspensionState.Suspending, Error = null };
                    currentTarget = currentTarget with { Processes = states.ToArray() };
                    ReplaceTarget(targets, currentTarget);
                    session = new SuspensionSession(2, targets.ToArray());
                    _store.Save(session);
                    try
                    {
                        _nativeApi.Suspend(states[index].Identity);
                        states[index] = states[index] with { State = SuspensionState.Suspended, Error = null };
                        targetSuspendedCount++;
                        suspendedProcesses++;
                        if (trimWorkingSets)
                        {
                            try
                            {
                                var trimResult = _nativeApi.TrimWorkingSet(states[index].Identity.ProcessId);
                                states[index] = states[index] with
                                {
                                    WorkingSetBeforeTrimBytes = trimResult.BeforeBytes,
                                    WorkingSetAfterTrimBytes = trimResult.AfterBytes,
                                    TrimError = null
                                };
                                trimmedProcesses++;
                            }
                            catch (Exception exception)
                            {
                                states[index] = states[index] with { TrimError = exception.Message };
                                _store.Log($"Working-set trim failed for {states[index].Identity.Name} ({states[index].Identity.ProcessId}): {exception.Message}");
                            }
                        }
                    }
                    catch (ProcessIdentityMismatchException exception)
                    {
                        states[index] = states[index] with { State = SuspensionState.Skipped, Error = exception.Message };
                        _store.Log($"Suspend skipped for {states[index].Identity.Name} ({states[index].Identity.ProcessId}): {exception.Message}");
                    }
                    catch (Exception exception)
                    {
                        states[index] = states[index] with { State = SuspensionState.Failed, Error = exception.Message };
                        _store.Log($"Suspend failed for {states[index].Identity.Name} ({states[index].Identity.ProcessId}): {exception.Message}");
                    }

                    currentTarget = currentTarget with { Processes = states.ToArray() };
                    ReplaceTarget(targets, currentTarget);
                    session = new SuspensionSession(2, targets.ToArray());
                    _store.Save(session);
                }

                if (targetSuspendedCount > 0)
                {
                    suspendedTargets++;
                }
                else
                {
                    targets.RemoveAll(item => item.TargetId == currentTarget.TargetId);
                    session = new SuspensionSession(2, targets.ToArray());
                    SaveOrClear(session);
                }
            }

            var success = suspendedTargets > 0;
            var message = success && trimWorkingSets
                ? $"已深度暂停 {suspendedTargets} 个程序；{trimmedProcesses}/{suspendedProcesses} 个相关进程完成工作集回收。"
                : success
                ? $"已暂停 {suspendedTargets} 个程序，共 {suspendedProcesses} 个相关进程。"
                : "Windows 未允许暂停任何已选进程。";
            return new OperationResult(success, message, targets.Count > 0 ? session : null);
        }
    }

    public OperationResult ResumeTargets(IEnumerable<Guid> targetIds)
    {
        lock (_syncRoot)
        {
            var requested = targetIds.ToHashSet();
            return ResumeMatching(target => requested.Contains(target.TargetId));
        }
    }

    public OperationResult ResumeActiveSession()
    {
        lock (_syncRoot)
        {
            return ResumeMatching(_ => true);
        }
    }

    public OperationResult ReconcileActiveSession()
    {
        lock (_syncRoot)
        {
            var session = _store.Load();
            if (session is null)
            {
                if (_store.LastLoadError is not null)
                    return new OperationResult(false, _store.LastLoadError);
                return new OperationResult(true, "没有检测到暂停记录。");
            }

            var verifiedTargets = new List<SuspendedTarget>();
            foreach (var target in session.Targets)
            {
                var states = target.Processes.ToArray();
                for (var index = 0; index < states.Length; index++)
                {
                    if (states[index].State == SuspensionState.Planned)
                    {
                        states[index] = states[index] with
                        {
                            State = SuspensionState.Resumed,
                            Error = "暂停操作尚未执行。"
                        };
                        continue;
                    }
                    if (states[index].State is not (SuspensionState.Suspending or SuspensionState.Suspended))
                    {
                        continue;
                    }

                    if (!_catalog.IsSameProcess(states[index].Identity))
                    {
                        states[index] = states[index] with
                        {
                            State = SuspensionState.Skipped,
                            Error = "进程已退出或 PID 已被复用。"
                        };
                        continue;
                    }

                    try
                    {
                        states[index] = _nativeApi.IsSuspended(states[index].Identity.ProcessId)
                            ? states[index] with { State = SuspensionState.Suspended, Error = null }
                            : states[index] with { State = SuspensionState.Resumed, Error = "检测到进程并未处于暂停状态。" };
                    }
                    catch (Exception exception)
                    {
                        states[index] = states[index] with { Error = $"状态检测失败：{exception.Message}" };
                    }
                }

                if (states.Any(state => state.State is SuspensionState.Suspending or SuspensionState.Suspended))
                {
                    verifiedTargets.Add(target with { Processes = states });
                }
            }

            var verified = new SuspensionSession(2, verifiedTargets.ToArray());
            SaveOrClear(verified);
            var message = verifiedTargets.Count > 0
                ? $"检测到 {verifiedTargets.Count} 个仍处于暂停状态的程序。"
                : "暂停记录已校验，未发现仍被暂停的程序。";
            return new OperationResult(true, message, verifiedTargets.Count > 0 ? verified : null);
        }
    }

    private OperationResult ResumeMatching(Func<SuspendedTarget, bool> predicate)
    {
        var session = _store.Load();
        if (session is null)
        {
            if (_store.LastLoadError is not null)
                return new OperationResult(false, _store.LastLoadError);
            return new OperationResult(true, "没有暂停中的程序。");
        }

        var targets = session.Targets.ToList();
        var selectedTargets = targets.Where(predicate).ToArray();
        if (selectedTargets.Length == 0)
        {
            return new OperationResult(false, "请先勾选需要恢复的程序。", session);
        }

        var resumedTargets = 0;
        var resumedProcesses = 0;
        var failedProcesses = 0;
        foreach (var target in selectedTargets)
        {
            var states = target.Processes.ToArray();
            foreach (var indexed in states
                         .Select((state, index) => (state, index))
                         .Where(item => item.state.State is SuspensionState.Suspending or SuspensionState.Suspended)
                         .OrderBy(item => item.state.Identity.Depth)
                         .ThenBy(item => item.state.Identity.ProcessId))
            {
                if (!_catalog.IsSameProcess(indexed.state.Identity))
                {
                    states[indexed.index] = indexed.state with
                    {
                        State = SuspensionState.Skipped,
                        Error = "进程已退出或 PID 已被复用。"
                    };
                    continue;
                }

                try
                {
                    _nativeApi.Resume(indexed.state.Identity);
                    states[indexed.index] = indexed.state with { State = SuspensionState.Resumed, Error = null };
                    resumedProcesses++;
                }
                catch (ProcessIdentityMismatchException exception)
                {
                    states[indexed.index] = indexed.state with
                    {
                        State = SuspensionState.Skipped,
                        Error = exception.Message
                    };
                }
                catch (Exception exception)
                {
                    states[indexed.index] = indexed.state with { Error = exception.Message };
                    failedProcesses++;
                    _store.Log($"Resume failed for {indexed.state.Identity.Name} ({indexed.state.Identity.ProcessId}): {exception.Message}");
                }

                var updatedTarget = target with { Processes = states.ToArray() };
                ReplaceTarget(targets, updatedTarget);
                _store.Save(new SuspensionSession(2, targets.ToArray()));
            }

            if (states.All(state => state.State is not (SuspensionState.Suspending or SuspensionState.Suspended)))
            {
                targets.RemoveAll(item => item.TargetId == target.TargetId);
                resumedTargets++;
                _store.Log($"Resume completed for {target.TargetName}; {states.Length} process(es).");
            }
        }

        var updatedSession = new SuspensionSession(2, targets.ToArray());
        SaveOrClear(updatedSession);
        var success = failedProcesses == 0;
        var message = success
            ? $"已恢复 {resumedTargets} 个程序，共 {resumedProcesses} 个相关进程。"
            : $"已恢复 {resumedProcesses} 个进程，仍有 {failedProcesses} 个进程需要处理。";
        return new OperationResult(success, message, targets.Count > 0 ? updatedSession : null);
    }

    private void SaveOrClear(SuspensionSession session)
    {
        if (session.Targets.Count == 0)
        {
            _store.Clear();
        }
        else
        {
            _store.Save(session);
        }
    }

    private static void ReplaceTarget(List<SuspendedTarget> targets, SuspendedTarget target)
    {
        var index = targets.FindIndex(item => item.TargetId == target.TargetId);
        if (index >= 0)
        {
            targets[index] = target;
        }
    }
}
