using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MultiWiz.Platform.Windows;

/// <summary>
/// A dedicated background thread that owns a message-only window and pumps its messages.
/// Global hotkeys (<c>WM_HOTKEY</c>) and out-of-context WinEvent hooks are delivered to this thread, and
/// other platform services marshal work onto it with <see cref="Post"/>, <see cref="Invoke(Action)"/> and
/// <see cref="InvokeAsync{T}"/>.
/// </summary>
internal sealed unsafe class Win32MessageThread : IDisposable
{
    private const uint InvokeMessage = PInvoke.WM_USER + 1;
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly ILogger<Win32MessageThread> _logger;

    // The window class keeps a native pointer to this delegate, so it must stay referenced.
    private readonly WNDPROC _windowProcedure;
    private readonly string _className = $"MultiWiz.MessageWindow.{Guid.NewGuid():N}";
    private readonly Thread _thread;
    private readonly int _managedThreadId;
    private readonly Lock _gate = new();
    private readonly Queue<QueuedWork> _queue = new();
    private bool _accepting;
    private bool _drainPosted;
    private bool _disposeRequested;
    private HWND _window;

    public Win32MessageThread(ILogger<Win32MessageThread> logger)
    {
        _logger = logger;
        _windowProcedure = WindowProcedure;

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _thread = new Thread(() => Run(started))
        {
            IsBackground = true,
            Name = "MultiWiz message loop",
        };
        _managedThreadId = _thread.ManagedThreadId;
        _thread.Start();

        try
        {
            started.Task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("MultiWiz could not create its Win32 message window.", ex);
        }
    }

    /// <summary>Raised on the message thread with the id of a hotkey registered on <see cref="WindowHandle"/>.</summary>
    public event Action<int>? HotkeyPressed;

    /// <summary>The message-only window. Hotkeys must be registered on it from the message thread.</summary>
    public HWND WindowHandle => _window;

    public bool IsMessageThread => Environment.CurrentManagedThreadId == _managedThreadId;

    /// <summary>
    /// Queues <paramref name="action"/> to run on the message thread and returns immediately. If the message thread
    /// cannot be woken, the action stays queued and runs with the next work that gets through.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The message thread has stopped.</exception>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Enqueue(new QueuedWork(action, Abandon: null));
    }

    /// <summary>
    /// Runs <paramref name="function"/> on the message thread (inline when already on it) and completes with its result.
    /// The task faults with a <see cref="Win32Exception"/> if the work could not be handed to the message thread.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The message thread has stopped.</exception>
    public Task<T> InvokeAsync<T>(Func<T> function)
    {
        ArgumentNullException.ThrowIfNull(function);

        if (IsMessageThread)
        {
            try
            {
                return Task.FromResult(function());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new QueuedWork(
            () =>
            {
                try
                {
                    completion.TrySetResult(function());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            },
            error => completion.TrySetException(error)));
        return completion.Task;
    }

    /// <summary>Runs <paramref name="function"/> on the message thread and waits for its result.</summary>
    /// <exception cref="ObjectDisposedException">The message thread has stopped.</exception>
    /// <exception cref="Win32Exception">The work could not be handed to the message thread.</exception>
    public T Invoke<T>(Func<T> function) => InvokeAsync(function).GetAwaiter().GetResult();

    /// <summary>Runs <paramref name="action"/> on the message thread and waits for it to finish.</summary>
    /// <exception cref="ObjectDisposedException">The message thread has stopped.</exception>
    /// <exception cref="Win32Exception">The work could not be handed to the message thread.</exception>
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Invoke<bool>(() =>
        {
            action();
            return true;
        });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposeRequested)
            {
                return;
            }

            _disposeRequested = true;
        }

        if (IsMessageThread)
        {
            PInvoke.PostQuitMessage(0);
            return;
        }

        try
        {
            Post(static () => PInvoke.PostQuitMessage(0));
        }
        catch (ObjectDisposedException)
        {
            // The loop already ended.
            return;
        }

        if (!_thread.Join(ShutdownTimeout))
        {
            _logger.LogWarning("The Win32 message thread did not stop within {Timeout}.", ShutdownTimeout);
        }
    }

    private void Enqueue(QueuedWork work)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(!_accepting, this);
            _queue.Enqueue(work);
            if (_drainPosted)
            {
                return;
            }

            _drainPosted = true;
        }

        if (PInvoke.PostMessage(_window, InvokeMessage, default, default))
        {
            return;
        }

        // Typically ERROR_NOT_ENOUGH_QUOTA: the loop is blocked and its message queue is full. No drain is coming, so
        // fail every caller that waits on queued work instead of leaving it waiting forever. Fire-and-forget work stays
        // queued and runs with the next work that gets through.
        var error = new Win32Exception(Marshal.GetLastPInvokeError(), "Could not post work to the Win32 message thread.");
        QueuedWork[] abandoned;
        lock (_gate)
        {
            _drainPosted = false;
            abandoned = _queue.Where(static item => item.Abandon is not null).ToArray();
            if (abandoned.Length > 0)
            {
                var remaining = _queue.Where(static item => item.Abandon is null).ToArray();
                _queue.Clear();
                foreach (var item in remaining)
                {
                    _queue.Enqueue(item);
                }
            }
        }

        _logger.LogError(error, "Could not post work to the Win32 message thread; {Count} waiting callers were failed.", abandoned.Length);
        foreach (var item in abandoned)
        {
            item.Abandon?.Invoke(error);
        }
    }

    private void Run(TaskCompletionSource started)
    {
        try
        {
            CreateMessageWindow();
        }
        catch (Exception ex)
        {
            started.TrySetException(ex);
            return;
        }

        lock (_gate)
        {
            _accepting = true;
        }

        started.TrySetResult();

        try
        {
            PumpMessages();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "The Win32 message loop failed.");
        }
        finally
        {
            lock (_gate)
            {
                _accepting = false;
            }

            // Run whatever was queued before the loop ended so no caller waits forever.
            DrainQueue();
            PInvoke.DestroyWindow(_window);
        }
    }

    private void CreateMessageWindow()
    {
        using var instance = PInvoke.GetModuleHandle((string?)null);

        fixed (char* className = _className)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _windowProcedure,
                hInstance = (HINSTANCE)instance.DangerousGetHandle(),
                lpszClassName = className,
            };

            if (PInvoke.RegisterClassEx(in windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        _window = PInvoke.CreateWindowEx(0, _className, "MultiWiz", 0, 0, 0, 0, 0, HWND.HWND_MESSAGE, null, instance, null);
        if (_window.IsNull)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }

    private static void PumpMessages()
    {
        while (true)
        {
            int result = PInvoke.GetMessage(out MSG message, HWND.Null, 0, 0);
            if (result == 0)
            {
                return; // WM_QUIT
            }

            if (result == -1)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            PInvoke.TranslateMessage(in message);
            PInvoke.DispatchMessage(in message);
        }
    }

    private LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        // Exceptions must never unwind into native code.
        try
        {
            switch (message)
            {
                case InvokeMessage:
                    DrainQueue();
                    return new LRESULT(0);
                case PInvoke.WM_HOTKEY:
                    HotkeyPressed?.Invoke((int)wParam.Value);
                    return new LRESULT(0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on the Win32 message thread.");
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private void DrainQueue()
    {
        QueuedWork[] work;
        lock (_gate)
        {
            _drainPosted = false;
            if (_queue.Count == 0)
            {
                return;
            }

            work = _queue.ToArray();
            _queue.Clear();
        }

        foreach (var item in work)
        {
            try
            {
                item.Run();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A queued action failed on the Win32 message thread.");
            }
        }
    }

    /// <param name="Run">The work, run on the message thread.</param>
    /// <param name="Abandon">Fails the waiting caller when the work cannot be delivered; null for fire-and-forget work.</param>
    private sealed record QueuedWork(Action Run, Action<Exception>? Abandon);
}
