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

    /// <summary>
    /// True when another MultiWiz already runs in this session. Only a hint (the instance may be exiting); use
    /// <see cref="TryAcquire"/> to decide who runs.
    /// </summary>
    public static bool IsAnotherInstanceRunning()
    {
        try
        {
            if (Mutex.TryOpenExisting(MutexName, out var existing))
            {
                existing.Dispose();
                return true;
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true; // Created by an elevated MultiWiz.
        }
    }

    /// <summary>Returns the guard when this is the first instance, or null when another instance already runs.</summary>
    public static SingleInstanceGuard? TryAcquire()
    {
        Mutex mutex;
        bool createdNew;
        try
        {
            mutex = new Mutex(initiallyOwned: true, MutexName, out createdNew);
        }
        catch (UnauthorizedAccessException)
        {
            // A MultiWiz started with "Run as administrator" owns the mutex, and a normal process may not open it.
            return null;
        }

        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        try
        {
            var activation = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ActivationEventName);
            return new SingleInstanceGuard(mutex, activation);
        }
        catch (UnauthorizedAccessException)
        {
            // An elevated MultiWiz that is still exiting holds the activation event.
            mutex.ReleaseMutex();
            mutex.Dispose();
            return null;
        }
    }

    /// <summary>Asks the running instance to show itself. Safe to call when it is shutting down.</summary>
    public static void SignalRunningInstance()
    {
        // This process was just started by the user, so it may hand foreground rights to the running instance.
        _ = PInvoke.AllowSetForegroundWindow(PInvoke.ASFW_ANY);

        try
        {
            if (EventWaitHandle.TryOpenExisting(ActivationEventName, out var activation))
            {
                using (activation)
                {
                    activation.Set();
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // The running copy is elevated, and a normal process cannot signal it.
            NativeDialog.ShowInformation(
                "MultiWiz is already running as administrator. Open it from its tray icon, or start this shortcut " +
                "with \"Run as administrator\" too.");
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
