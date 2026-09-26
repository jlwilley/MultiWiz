using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Platform;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MultiWiz.Platform.Windows.Audio;

/// <summary>
/// Per-process volume through Windows audio sessions. A dedicated MTA thread owns every NAudio COM object;
/// public methods queue work for it (<see cref="Release"/> and <see cref="RestoreAll"/> then wait for theirs, with a
/// timeout), and a newer <see cref="Apply"/> replaces any queued older one.
/// </summary>
internal sealed class AudioService : IAudioService
{
    // A process with no known session triggers a device re-enumeration at most this often.
    private static readonly TimeSpan EnumerationInterval = TimeSpan.FromMilliseconds(750);

    // Cached processes are re-enumerated this often anyway, to pick up sessions on newly used devices.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    // A just-launched client may open its audio session a while after it gets focus or loses it.
    private static readonly TimeSpan RetryWindow = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RetryPollInterval = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReleaseTimeout = TimeSpan.FromSeconds(2);

    private readonly ILogger<AudioService> _logger;
    private readonly Thread _worker;
    private readonly AutoResetEvent _signal = new(false);
    private readonly Lock _gate = new();
    private readonly List<WorkItem> _queue = new();
    private bool _stopping;

    // Worker thread only.
    private readonly Dictionary<int, ProcessAudio> _processes = new();

    // Worker thread only. The first volume seen for each session identifier (executable, device and session GUID),
    // which is the key Windows saves session volumes under. A client killed while muted makes Windows save the
    // mute, so the next client of that executable starts muted; it is restored to this value instead.
    private readonly Dictionary<string, float> _originalsBySessionIdentifier = new(StringComparer.OrdinalIgnoreCase);
    private MMDeviceEnumerator? _enumerator;

    public AudioService(ILogger<AudioService> logger)
    {
        _logger = logger;
        _worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "MultiWiz audio",
        };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
    }

    public void Apply(IReadOnlyList<VolumeTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var snapshot = targets.ToArray();

        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            // Each Apply carries the complete desired state, so older queued ones are obsolete.
            _queue.RemoveAll(static item => item is ApplyWork);
            _queue.Add(new ApplyWork(snapshot));
            _signal.Set();
        }
    }

    public void Release(int processId)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            _queue.Add(new ReleaseWork(processId, completion));
            _signal.Set();
        }

        // Waits so a caller that is about to end the process restores its volume while the session still exists.
        if (!completion.Task.Wait(ReleaseTimeout))
        {
            _logger.LogWarning("Restoring the volume of process {ProcessId} did not finish within {Timeout}.", processId, ReleaseTimeout);
        }
    }

    public void RestoreAll()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            _queue.Add(new RestoreAllWork(completion));
            _signal.Set();
        }

        if (!completion.Task.Wait(RestoreTimeout))
        {
            _logger.LogWarning("Restoring client volumes did not finish within {Timeout}.", RestoreTimeout);
        }
    }

    public void Dispose()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            // Put every volume back before the worker exits.
            _stopping = true;
            _queue.Add(new RestoreAllWork(completion));
            _signal.Set();
        }

        if (_worker.Join(RestoreTimeout))
        {
            _signal.Dispose();
        }
        else
        {
            _logger.LogWarning("The audio worker did not stop within {Timeout}.", RestoreTimeout);
        }
    }

    private void Run()
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Audio devices are unavailable; per-client volume control is disabled.");
        }

        while (true)
        {
            WorkItem[] batch;
            bool stopping;
            lock (_gate)
            {
                batch = _queue.ToArray();
                _queue.Clear();
                stopping = _stopping;
            }

            foreach (var item in batch)
            {
                Execute(item);
            }

            if (stopping)
            {
                break;
            }

            var timeout = HasPendingRetries() ? RetryPollInterval : Timeout.InfiniteTimeSpan;
            if (!_signal.WaitOne(timeout))
            {
                try
                {
                    RetryUnresolved();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Retrying audio sessions that were not found yet failed.");
                }
            }
        }

        foreach (var state in _processes.Values)
        {
            state.DisposeSessions();
        }

        _processes.Clear();
        _originalsBySessionIdentifier.Clear();
        _enumerator?.Dispose();
        _enumerator = null;
    }

    private void Execute(WorkItem item)
    {
        try
        {
            switch (item)
            {
                case ApplyWork apply:
                    ApplyTargets(apply.Targets);
                    break;
                case ReleaseWork release:
                    ReleaseProcess(release.ProcessId);
                    break;
                case RestoreAllWork _:
                    RestoreEverything();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audio work item failed.");
        }
        finally
        {
            switch (item)
            {
                case ReleaseWork release:
                    release.Completion.TrySetResult();
                    break;
                case RestoreAllWork restore:
                    restore.Completion.TrySetResult();
                    break;
            }
        }
    }

    private void ApplyTargets(IReadOnlyList<VolumeTarget> targets)
    {
        if (_enumerator is null)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var states = new List<ProcessAudio>(targets.Count);
        foreach (var target in targets)
        {
            if (target.ProcessId <= 0)
            {
                continue;
            }

            if (!_processes.TryGetValue(target.ProcessId, out var state))
            {
                state = new ProcessAudio(target.ProcessId);
                _processes.Add(target.ProcessId, state);
            }

            state.DesiredVolume = Math.Clamp(target.VolumePercent, 0, 100) / 100f;
            state.DesiredSince = now;
            states.Add(state);
        }

        EnsureSessions(states.Where(state => NeedsEnumeration(state, now)).ToList());
        foreach (var state in states)
        {
            SetDesiredVolume(state);
        }
    }

    private bool HasPendingRetries() => _processes.Values.Any(IsAwaitingSession);

    private void RetryUnresolved()
    {
        var now = Stopwatch.GetTimestamp();
        var unresolved = _processes.Values.Where(IsAwaitingSession).ToList();
        EnsureSessions(unresolved.Where(state => NeedsEnumeration(state, now)).ToList());
        foreach (var state in unresolved)
        {
            SetDesiredVolume(state);
        }
    }

    private static bool IsAwaitingSession(ProcessAudio state) =>
        state.DesiredVolume is not null && state.Sessions.Count == 0 &&
        Stopwatch.GetElapsedTime(state.DesiredSince) < RetryWindow;

    private static bool NeedsEnumeration(ProcessAudio state, long now)
    {
        if (state.LastEnumeration == 0)
        {
            return true;
        }

        var sinceLast = Stopwatch.GetElapsedTime(state.LastEnumeration, now);
        return sinceLast >= EnumerationInterval && (state.Sessions.Count == 0 || sinceLast >= RefreshInterval);
    }

    /// <summary>Enumerates the sessions of every active render device once and caches those of the given processes.</summary>
    private void EnsureSessions(IReadOnlyList<ProcessAudio> states)
    {
        if (states.Count == 0 || _enumerator is null)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var byProcessId = new Dictionary<int, ProcessAudio>(states.Count);
        foreach (var state in states)
        {
            state.LastEnumeration = now;
            byProcessId[state.ProcessId] = state;
        }

        using var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var enumeratedDevice in devices)
        {
            using var device = enumeratedDevice;
            try
            {
                CollectSessions(device, byProcessId);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read the audio sessions of a playback device.");
            }
        }
    }

    private void CollectSessions(MMDevice device, Dictionary<int, ProcessAudio> byProcessId)
    {
        var sessions = device.AudioSessionManager.Sessions;
        var count = sessions.Count;
        for (var i = 0; i < count; i++)
        {
            var session = sessions[i];
            var keep = false;
            try
            {
                if (!session.IsSystemSoundsSession
                    && session.State != AudioSessionState.AudioSessionStateExpired
                    && byProcessId.TryGetValue((int)session.GetProcessID, out var state))
                {
                    keep = state.TryAdd(session, _originalsBySessionIdentifier);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skipping an audio session that could not be inspected.");
            }
            finally
            {
                if (!keep)
                {
                    session.Dispose();
                }
            }
        }
    }

    private void SetDesiredVolume(ProcessAudio state)
    {
        if (state.DesiredVolume is not { } volume)
        {
            return;
        }

        for (var i = state.Sessions.Count - 1; i >= 0; i--)
        {
            var tracked = state.Sessions[i];
            try
            {
                if (tracked.Control.State == AudioSessionState.AudioSessionStateExpired)
                {
                    state.DropSessionAt(i);
                    continue;
                }

                if (tracked.Control.SimpleAudioVolume is { } simpleVolume)
                {
                    simpleVolume.Volume = volume;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Dropping an audio session of process {ProcessId} that stopped responding.", state.ProcessId);
                state.DropSessionAt(i);
            }
        }
    }

    private void ReleaseProcess(int processId)
    {
        if (_processes.Remove(processId, out var state))
        {
            Restore(state);
            state.DisposeSessions();
        }
    }

    private void RestoreEverything()
    {
        foreach (var state in _processes.Values)
        {
            Restore(state);
            state.DisposeSessions();
        }

        _processes.Clear();
    }

    private void Restore(ProcessAudio state)
    {
        if (state.OriginalVolumes.Count == 0)
        {
            return;
        }

        // Sessions dropped from the cache still need their volume back if the process is running.
        if (state.HasUncachedOriginals)
        {
            try
            {
                EnsureSessions([state]);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not re-enumerate audio sessions for process {ProcessId}.", state.ProcessId);
            }
        }

        foreach (var tracked in state.Sessions)
        {
            if (!state.OriginalVolumes.TryGetValue(tracked.Key, out var original))
            {
                continue;
            }

            try
            {
                if (tracked.Control.SimpleAudioVolume is { } simpleVolume)
                {
                    simpleVolume.Volume = original;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not restore an audio session of process {ProcessId}.", state.ProcessId);
            }
        }
    }

    private abstract record WorkItem;

    private sealed record ApplyWork(IReadOnlyList<VolumeTarget> Targets) : WorkItem;

    private sealed record ReleaseWork(int ProcessId, TaskCompletionSource Completion) : WorkItem;

    private sealed record RestoreAllWork(TaskCompletionSource Completion) : WorkItem;

    private sealed record TrackedSession(AudioSessionControl Control, string Key);

    /// <summary>Audio state of one process. Worker thread only.</summary>
    private sealed class ProcessAudio(int processId)
    {
        private readonly List<TrackedSession> _sessions = new();

        public int ProcessId { get; } = processId;

        public IReadOnlyList<TrackedSession> Sessions => _sessions;

        /// <summary>Volume of each session (by instance identifier) before MultiWiz first changed it.</summary>
        public Dictionary<string, float> OriginalVolumes { get; } = new(StringComparer.Ordinal);

        public float? DesiredVolume { get; set; }

        public long DesiredSince { get; set; }

        public long LastEnumeration { get; set; }

        public bool HasUncachedOriginals => OriginalVolumes.Keys.Any(key => _sessions.All(s => s.Key != key));

        /// <summary>
        /// Takes ownership of <paramref name="session"/> unless it is already cached. The original volume comes from
        /// <paramref name="originalsBySessionIdentifier"/> when another client of the same executable was seen first.
        /// </summary>
        public bool TryAdd(AudioSessionControl session, Dictionary<string, float> originalsBySessionIdentifier)
        {
            var key = session.GetSessionInstanceIdentifier;
            if (string.IsNullOrEmpty(key) || _sessions.Any(s => s.Key == key))
            {
                return false;
            }

            if (!OriginalVolumes.ContainsKey(key) && session.SimpleAudioVolume is { } simpleVolume)
            {
                var group = session.GetSessionIdentifier;
                if (string.IsNullOrEmpty(group))
                {
                    OriginalVolumes[key] = simpleVolume.Volume;
                }
                else if (originalsBySessionIdentifier.TryGetValue(group, out var groupOriginal))
                {
                    OriginalVolumes[key] = groupOriginal;
                }
                else
                {
                    var current = simpleVolume.Volume;
                    originalsBySessionIdentifier[group] = current;
                    OriginalVolumes[key] = current;
                }
            }

            _sessions.Add(new TrackedSession(session, key));
            return true;
        }

        public void DropSessionAt(int index)
        {
            var tracked = _sessions[index];
            _sessions.RemoveAt(index);
            DisposeQuietly(tracked.Control);
        }

        public void DisposeSessions()
        {
            foreach (var tracked in _sessions)
            {
                DisposeQuietly(tracked.Control);
            }

            _sessions.Clear();
        }

        private static void DisposeQuietly(AudioSessionControl control)
        {
            try
            {
                control.Dispose();
            }
            catch (Exception)
            {
                // The session's audio endpoint is already gone; nothing is left to release.
            }
        }
    }
}
