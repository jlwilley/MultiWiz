using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Hotkeys;
using MultiWiz.Core.Platform;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace MultiWiz.Platform.Windows.Input;

/// <summary>
/// Global hotkeys registered with <c>RegisterHotKey</c> on the <see cref="Win32MessageThread"/> window.
/// Callbacks run on the message thread, which holds foreground-activation rights while handling <c>WM_HOTKEY</c>.
/// </summary>
internal sealed class HotkeyService : IHotkeyService
{
    // Application hotkey ids must be in the range 0x0000..0xBFFF.
    private const int MaxHotkeyId = 0xBFFF;

    private readonly Win32MessageThread _messageThread;
    private readonly ILogger<HotkeyService> _logger;

    // Only touched on the message thread.
    private readonly Dictionary<int, Action> _callbacks = new();
    private int _nextId = 1;

    private int _disposed;

    public HotkeyService(Win32MessageThread messageThread, ILogger<HotkeyService> logger)
    {
        _messageThread = messageThread;
        _logger = logger;
        _messageThread.HotkeyPressed += OnHotkeyPressed;
    }

    public IDisposable? TryRegister(HotkeyBinding binding, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (!binding.IsValid)
        {
            _logger.LogDebug("Ignoring invalid hotkey binding {Binding}.", binding.ToString());
            return null;
        }

        int id;
        try
        {
            id = _messageThread.Invoke<int>(() => Register(binding, callback));
        }
        catch (Win32Exception ex)
        {
            _logger.LogWarning(ex, "Hotkey {Binding} was not registered because the message thread is not responding.", binding.ToString());
            return null;
        }

        return id == 0 ? null : new Registration(this, id);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _messageThread.HotkeyPressed -= OnHotkeyPressed;
        try
        {
            _messageThread.Invoke(UnregisterAll);
        }
        catch (ObjectDisposedException)
        {
            // The message window is gone, and its hotkeys with it.
        }
        catch (Win32Exception ex)
        {
            _logger.LogWarning(ex, "Global hotkeys could not be unregistered.");
        }
    }

    private int Register(HotkeyBinding binding, Action callback)
    {
        var id = AllocateId();
        if (id == 0)
        {
            _logger.LogWarning("No free hotkey ids are left; {Binding} was not registered.", binding.ToString());
            return 0;
        }

        var modifiers = (HOT_KEY_MODIFIERS)(uint)binding.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        if (!PInvoke.RegisterHotKey(_messageThread.WindowHandle, id, modifiers, (uint)binding.VirtualKey))
        {
            _logger.LogInformation(
                "Hotkey {Binding} is not available (error {Error}); another program probably uses it.",
                binding.ToString(),
                Marshal.GetLastPInvokeError());
            return 0;
        }

        _callbacks.Add(id, callback);
        return id;
    }

    private int AllocateId()
    {
        for (var attempt = 0; attempt < MaxHotkeyId; attempt++)
        {
            var id = _nextId;
            _nextId = _nextId >= MaxHotkeyId ? 1 : _nextId + 1;
            if (!_callbacks.ContainsKey(id))
            {
                return id;
            }
        }

        return 0;
    }

    private void Unregister(int id)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            _messageThread.Invoke(() => UnregisterOnMessageThread(id));
        }
        catch (ObjectDisposedException)
        {
            // The message window is gone, and its hotkeys with it.
        }
        catch (Win32Exception ex)
        {
            _logger.LogWarning(ex, "Hotkey {Id} could not be unregistered.", id);
        }
    }

    private void UnregisterOnMessageThread(int id)
    {
        if (_callbacks.Remove(id))
        {
            PInvoke.UnregisterHotKey(_messageThread.WindowHandle, id);
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _callbacks.Keys)
        {
            PInvoke.UnregisterHotKey(_messageThread.WindowHandle, id);
        }

        _callbacks.Clear();
    }

    private void OnHotkeyPressed(int id)
    {
        if (!_callbacks.TryGetValue(id, out var callback))
        {
            return;
        }

        try
        {
            callback();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A hotkey handler failed.");
        }
    }

    private sealed class Registration(HotkeyService owner, int id) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Unregister(id);
            }
        }
    }
}
