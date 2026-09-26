using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Primitives;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MultiWiz.Platform.Windows.Windowing;

/// <summary>
/// Out-of-context WinEvent hooks installed on the <see cref="Win32MessageThread"/>: one global foreground hook,
/// plus per-process location/minimize/destroy hooks shared by every tracked window of that process.
/// All hook state is only touched on the message thread.
/// </summary>
internal sealed class WindowEventsService : IWindowEvents
{
    private static readonly TimeSpan MinUpdateInterval = TimeSpan.FromSeconds(1.0 / 60);

    private readonly Win32MessageThread _messageThread;
    private readonly ILogger<WindowEventsService> _logger;

    // Native code calls these delegates for as long as the hooks exist, so they must stay referenced.
    private readonly WINEVENTPROC _foregroundCallback;
    private readonly WINEVENTPROC _trackingCallback;

    // Message thread only.
    private readonly Dictionary<nint, List<WindowTracker>> _trackersByWindow = new();
    private readonly Dictionary<uint, ProcessHooks> _processHooks = new();
    private UnhookWinEventSafeHandle? _foregroundHook;

    private int _disposed;

    public WindowEventsService(Win32MessageThread messageThread, ILogger<WindowEventsService> logger)
    {
        _messageThread = messageThread;
        _logger = logger;
        _foregroundCallback = OnForegroundEvent;
        _trackingCallback = OnTrackingEvent;

        _messageThread.Invoke(() =>
        {
            _foregroundHook = InstallHook(
                PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND, 0, _foregroundCallback);
        });
    }

    public event EventHandler<ForegroundChangedEventArgs>? ForegroundChanged;

    public IDisposable TrackWindow(nint hwnd, Action<WindowTrackingUpdate> onUpdate)
    {
        ArgumentNullException.ThrowIfNull(onUpdate);
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        var tracker = new WindowTracker(this, hwnd, onUpdate);

        // Attach asynchronously so a caller holding its own thread (e.g. the UI thread) never waits on the message
        // thread; the first update is delivered as soon as the hooks are in place.
        _messageThread.Post(() => Attach(tracker));
        return tracker;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _messageThread.Invoke(RemoveAllHooks);
        }
        catch (ObjectDisposedException)
        {
            // The message thread already ended; its hooks were removed with it.
        }
        catch (Win32Exception ex)
        {
            _logger.LogWarning(ex, "Window event hooks could not be removed.");
        }
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private void RemoveAllHooks()
    {
        foreach (var tracker in _trackersByWindow.Values.SelectMany(list => list))
        {
            tracker.OnDetached();
        }

        _trackersByWindow.Clear();

        foreach (var hooks in _processHooks.Values)
        {
            hooks.Dispose();
        }

        _processHooks.Clear();
        _foregroundHook?.Dispose();
        _foregroundHook = null;
    }

    private UnhookWinEventSafeHandle? InstallHook(uint eventMin, uint eventMax, uint processId, WINEVENTPROC callback)
    {
        var hook = PInvoke.SetWinEventHook(eventMin, eventMax, null, callback, processId, 0, PInvoke.WINEVENT_OUTOFCONTEXT);
        if (!hook.IsInvalid)
        {
            return hook;
        }

        hook.Dispose();
        _logger.LogWarning(
            "Could not install a window event hook (events 0x{EventMin:X}-0x{EventMax:X}, process {ProcessId}).",
            eventMin, eventMax, processId);
        return null;
    }

    private void Attach(WindowTracker tracker)
    {
        if (tracker.IsDisposed || IsDisposed)
        {
            return;
        }

        var threadId = PInvoke.GetWindowThreadProcessId((HWND)tracker.Window, out uint processId);
        if (threadId != 0)
        {
            if (!_trackersByWindow.TryGetValue(tracker.Window, out var trackers))
            {
                trackers = new List<WindowTracker>();
                _trackersByWindow.Add(tracker.Window, trackers);
            }

            trackers.Add(tracker);
            AcquireProcessHooks(processId);
            tracker.OnAttached(processId);
        }

        // Initial state. A window that is already gone reports IsClosed and is never attached.
        tracker.Flush();
    }

    private void Detach(WindowTracker tracker)
    {
        if (!tracker.IsAttached)
        {
            return;
        }

        if (_trackersByWindow.TryGetValue(tracker.Window, out var trackers))
        {
            trackers.Remove(tracker);
            if (trackers.Count == 0)
            {
                _trackersByWindow.Remove(tracker.Window);
            }
        }

        ReleaseProcessHooks(tracker.ProcessId);
        tracker.OnDetached();
    }

    private void AcquireProcessHooks(uint processId)
    {
        if (_processHooks.TryGetValue(processId, out var existing))
        {
            existing.References++;
            return;
        }

        var hooks = new ProcessHooks { References = 1 };
        hooks.Add(InstallHook(
            PInvoke.EVENT_OBJECT_LOCATIONCHANGE, PInvoke.EVENT_OBJECT_LOCATIONCHANGE, processId, _trackingCallback));
        hooks.Add(InstallHook(
            PInvoke.EVENT_SYSTEM_MINIMIZESTART, PInvoke.EVENT_SYSTEM_MINIMIZEEND, processId, _trackingCallback));
        hooks.Add(InstallHook(
            PInvoke.EVENT_OBJECT_DESTROY, PInvoke.EVENT_OBJECT_DESTROY, processId, _trackingCallback));
        _processHooks.Add(processId, hooks);
    }

    private void ReleaseProcessHooks(uint processId)
    {
        if (!_processHooks.TryGetValue(processId, out var hooks))
        {
            return;
        }

        if (--hooks.References <= 0)
        {
            hooks.Dispose();
            _processHooks.Remove(processId);
        }
    }

    private void OnForegroundEvent(
        HWINEVENTHOOK hook, uint eventType, HWND hwnd, int objectId, int childId, uint eventThread, uint eventTime)
    {
        // Exceptions must never unwind into native code.
        if (hwnd.IsNull)
        {
            return;
        }

        try
        {
            PInvoke.GetWindowThreadProcessId(hwnd, out uint processId);
            ForegroundChanged?.Invoke(this, new ForegroundChangedEventArgs((nint)hwnd, (int)processId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A foreground-change handler failed.");
        }

        try
        {
            // Every tracked window's IsForeground may have changed.
            foreach (var tracker in _trackersByWindow.Values.SelectMany(list => list).ToArray())
            {
                tracker.RequestUpdate();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A window-tracking handler failed.");
        }
    }

    private void OnTrackingEvent(
        HWINEVENTHOOK hook, uint eventType, HWND hwnd, int objectId, int childId, uint eventThread, uint eventTime)
    {
        try
        {
            // Location and destroy events also fire for child objects (caret, cursor, controls); only the window itself matters.
            var isWindowObject = objectId == (int)OBJECT_IDENTIFIER.OBJID_WINDOW && childId == (int)PInvoke.CHILDID_SELF;
            if (!isWindowObject && eventType is PInvoke.EVENT_OBJECT_LOCATIONCHANGE or PInvoke.EVENT_OBJECT_DESTROY)
            {
                return;
            }

            if (!_trackersByWindow.TryGetValue((nint)hwnd, out var trackers))
            {
                return;
            }

            foreach (var tracker in trackers.ToArray())
            {
                tracker.RequestUpdate();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A window-tracking handler failed.");
        }
    }

    private static WindowTrackingUpdate ReadState(nint hwnd)
    {
        if (!NativeWindowHelpers.IsAlive(hwnd))
        {
            return new WindowTrackingUpdate(null, false, false, true);
        }

        var window = (HWND)hwnd;
        bool minimized = PInvoke.IsIconic(window);
        var foreground = (nint)PInvoke.GetForegroundWindow() == hwnd;
        PixelRect? clientBounds = minimized ? null : NativeWindowHelpers.GetClientBounds(window);
        return new WindowTrackingUpdate(clientBounds, minimized, foreground, false);
    }

    private sealed class ProcessHooks : IDisposable
    {
        private readonly List<UnhookWinEventSafeHandle> _hooks = new();

        public int References { get; set; }

        public void Add(UnhookWinEventSafeHandle? hook)
        {
            if (hook is not null)
            {
                _hooks.Add(hook);
            }
        }

        public void Dispose()
        {
            foreach (var hook in _hooks)
            {
                hook.Dispose();
            }

            _hooks.Clear();
        }
    }

    /// <summary>One <see cref="TrackWindow"/> subscription. Everything except <see cref="Dispose"/> runs on the message thread.</summary>
    private sealed class WindowTracker : IDisposable
    {
        private readonly WindowEventsService _owner;
        private readonly Action<WindowTrackingUpdate> _onUpdate;
        private System.Threading.Timer? _timer;
        private long _lastFlushTimestamp;
        private bool _flushPending;
        private WindowTrackingUpdate? _lastDelivered;
        private int _disposed;

        public WindowTracker(WindowEventsService owner, nint window, Action<WindowTrackingUpdate> onUpdate)
        {
            _owner = owner;
            Window = window;
            _onUpdate = onUpdate;
        }

        public nint Window { get; }

        public uint ProcessId { get; private set; }

        public bool IsAttached { get; private set; }

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void OnAttached(uint processId)
        {
            ProcessId = processId;
            IsAttached = true;
        }

        public void OnDetached()
        {
            IsAttached = false;
            _flushPending = false;
            _timer?.Dispose();
            _timer = null;
        }

        /// <summary>Delivers the current state now, or schedules it so updates stay under ~60 per second.</summary>
        public void RequestUpdate()
        {
            if (IsDisposed || _flushPending)
            {
                return;
            }

            var elapsed = Stopwatch.GetElapsedTime(_lastFlushTimestamp);
            if (elapsed >= MinUpdateInterval)
            {
                Flush();
                return;
            }

            _flushPending = true;
            _timer ??= new System.Threading.Timer(OnTimer);
            _timer.Change(MinUpdateInterval - elapsed, Timeout.InfiniteTimeSpan);
        }

        public void Flush()
        {
            if (IsDisposed)
            {
                return;
            }

            _lastFlushTimestamp = Stopwatch.GetTimestamp();
            var state = ReadState(Window);
            if (_lastDelivered is { } previous && previous == state)
            {
                return;
            }

            _lastDelivered = state;
            try
            {
                _onUpdate(state);
            }
            catch (Exception ex)
            {
                _owner._logger.LogError(ex, "A window-tracking callback failed.");
            }

            if (state.IsClosed)
            {
                _owner.Detach(this);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                _owner._messageThread.Post(() => _owner.Detach(this));
            }
            catch (ObjectDisposedException)
            {
                // The message thread already ended; its hooks were removed with it.
            }
        }

        private void OnTimer(object? state)
        {
            try
            {
                _owner._messageThread.Post(FlushPending);
            }
            catch (ObjectDisposedException)
            {
                // Shutting down.
            }
        }

        private void FlushPending()
        {
            if (!_flushPending)
            {
                return;
            }

            _flushPending = false;
            Flush();
        }
    }
}
