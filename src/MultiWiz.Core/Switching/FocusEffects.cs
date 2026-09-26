using Microsoft.Extensions.Logging;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Settings;

namespace MultiWiz.Core.Switching;

/// <summary>
/// The alive client processes, those of them that finished logging in (<see cref="Sessions.ClientSessionState.Running"/>),
/// and the focused one, as seen by <see cref="ClientSwitcher"/>.
/// </summary>
internal readonly record struct FocusSnapshot(IReadOnlyList<int> ProcessIds, IReadOnlyList<int> RunningProcessIds, int? FocusedProcessId);

/// <summary>
/// Applies the audio and performance policies after focus changes. Calls to <see cref="Schedule"/> are debounced
/// (~120 ms, measured with <see cref="TimeProvider"/>) so rapid switching only applies the final state, and the work
/// runs on a timer callback rather than on the (time-critical) thread that changed focus.
/// </summary>
internal sealed class FocusEffects : IDisposable
{
    internal static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(120);

    private readonly IAudioService _audio;
    private readonly IProcessThrottler _throttler;
    private readonly ISettingsStore _settings;
    private readonly ILogger _logger;
    private readonly Func<FocusSnapshot> _snapshot;
    private readonly ITimer _timer;
    private readonly Lock _applyLock = new();
    private readonly Dictionary<int, ThrottleState> _throttled = new();
    private bool _audioApplied;
    private bool _disposed;

    public FocusEffects(
        IAudioService audio,
        IProcessThrottler throttler,
        ISettingsStore settings,
        TimeProvider timeProvider,
        ILogger logger,
        Func<FocusSnapshot> snapshot)
    {
        _audio = audio;
        _throttler = throttler;
        _settings = settings;
        _logger = logger;
        _snapshot = snapshot;
        _timer = timeProvider.CreateTimer(static state => ((FocusEffects)state!).Apply(), this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>(Re)starts the debounce window; the effects are applied once it passes without another call.</summary>
    public void Schedule()
    {
        try
        {
            _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Shutting down; nothing to apply any more.
        }
    }

    public void Dispose()
    {
        lock (_applyLock)
        {
            _disposed = true;
        }

        _timer.Dispose();
    }

    private void Apply()
    {
        lock (_applyLock)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                var settings = _settings.Current;
                var snapshot = _snapshot();
                ApplyAudio(settings.Audio, snapshot);
                ApplyPerformance(settings.Performance, snapshot);
            }
            catch (Exception ex)
            {
                // This runs on a timer thread, where an escaping exception would take the whole app down.
                _logger.LogError(ex, "Applying focus effects failed");
            }
        }
    }

    private void ApplyAudio(AudioSettings audio, FocusSnapshot snapshot)
    {
        if (audio.Enabled)
        {
            _audio.Apply(AudioPolicy.Compute(snapshot.ProcessIds, snapshot.FocusedProcessId, audio));
            _audioApplied = true;
        }
        else if (_audioApplied)
        {
            // Turned off: put every client back to its original volume, once.
            _audio.RestoreAll();
            _audioApplied = false;
        }
    }

    private void ApplyPerformance(PerformanceSettings performance, FocusSnapshot snapshot)
    {
        if (!performance.EfficiencyModeForBackground && !performance.LowerBackgroundPriority)
        {
            foreach (var processId in _throttled.Keys)
            {
                _throttler.Release(processId);
            }

            _throttled.Clear();
            return;
        }

        // Only clients that finished logging in are throttled: a client that is still loading is always in the
        // background while another one has focus, and slowing it down could make it miss the auto-login keystrokes.
        // A client that becomes Running raises SessionChanged, which schedules another pass. Processes that left the
        // set have exited and were already released by the session manager.
        var running = new HashSet<int>(snapshot.RunningProcessIds);
        foreach (var gone in _throttled.Keys.Where(processId => !running.Contains(processId)).ToArray())
        {
            _throttled.Remove(gone);
        }

        foreach (var processId in running)
        {
            // With no focused client nobody counts as background, matching the audio policy.
            var background = snapshot.FocusedProcessId is { } focused && processId != focused;
            var state = new ThrottleState(background, performance.EfficiencyModeForBackground, performance.LowerBackgroundPriority);
            if (_throttled.TryGetValue(processId, out var applied) && applied == state)
            {
                continue;
            }

            _throttler.Apply(processId, state.Background, state.EfficiencyMode, state.LowerPriority);
            _throttled[processId] = state;
        }
    }

    private readonly record struct ThrottleState(bool Background, bool EfficiencyMode, bool LowerPriority);
}
