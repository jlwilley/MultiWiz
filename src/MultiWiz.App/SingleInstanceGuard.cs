using Windows.Win32;

namespace MultiWiz.App;

/// <summary>
/// Keeps MultiWiz to one running instance per user session. A second launch signals the first one (which then
/// shows its main window) and exits.
/// </summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\MultiWiz.SingleInstance";
    private const string ActivationEventName = @"Local\MultiWiz.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private RegisteredWaitHandle? _registration;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle activation)
    {
        _mutex = mutex;
        _activation = activation;
    }

    /// <summary>Returns the guard when this is the first instance, or null when another instance already runs.</summary>
    public static SingleInstanceGuard? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        var activation = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ActivationEventName);
        return new SingleInstanceGuard(mutex, activation);
    }

    /// <summary>Asks the running instance to show itself. Safe to call when it is shutting down.</summary>
    public static void SignalRunningInstance()
    {
        // This process was just started by the user, so it may hand foreground rights to the running instance.
        _ = PInvoke.AllowSetForegroundWindow(PInvoke.ASFW_ANY);

        if (EventWaitHandle.TryOpenExisting(ActivationEventName, out var activation))
        {
            using (activation)
            {
                activation.Set();
            }
        }
    }

    /// <summary>Invokes <paramref name="onActivated"/> on a thread-pool thread whenever another launch signals this instance.</summary>
    public void ListenForActivation(Action onActivated)
    {
        ArgumentNullException.ThrowIfNull(onActivated);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _registration?.Unregister(null);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activation,
            (_, _) => onActivated(),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registration?.Unregister(null);
        _activation.Dispose();

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Released from a different thread than the one that acquired it; closing the handle releases it anyway.
        }

        _mutex.Dispose();
    }
}
