using Avalonia.Threading;

namespace MultiWiz.App.Services;

/// <summary>
/// Runs the most recently scheduled action once nothing new has been scheduled for <c>delay</c>.
/// Used to batch rapid edits (typing, slider drags) into one save. UI thread only.
/// </summary>
public sealed class UiDebouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private DispatcherTimer? _timer;
    private Action? _pending;

    public UiDebouncer(TimeSpan delay)
    {
        _delay = delay;
    }

    public bool HasPending => _pending is not null;

    public void Schedule(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Dispatcher.UIThread.VerifyAccess();

        _pending = action;
        _timer ??= CreateTimer();
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Runs the pending action now (if any).</summary>
    public void Flush()
    {
        _timer?.Stop();
        var action = _pending;
        _pending = null;
        action?.Invoke();
    }

    public void Cancel()
    {
        _timer?.Stop();
        _pending = null;
    }

    public void Dispose()
    {
        Cancel();
        if (_timer is not null)
        {
            _timer.Tick -= OnTick;
            _timer = null;
        }
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer { Interval = _delay };
        timer.Tick += OnTick;
        return timer;
    }

    private void OnTick(object? sender, EventArgs e) => Flush();
}
