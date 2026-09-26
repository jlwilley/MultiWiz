using Avalonia.Threading;

namespace MultiWiz.App.Services;

/// <summary>
/// Collapses change notifications raised on any thread (Core events) into a single run of <c>action</c> on the UI
/// thread. A request made while the action runs schedules another run, so no change is missed.
/// </summary>
public sealed class UiCoalescer
{
    private readonly Action _action;
    private int _queued;

    public UiCoalescer(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _action = action;
    }

    public void Request()
    {
        if (Interlocked.Exchange(ref _queued, 1) == 0)
        {
            Dispatcher.UIThread.Post(Run);
        }
    }

    private void Run()
    {
        Volatile.Write(ref _queued, 0);
        _action();
    }
}
