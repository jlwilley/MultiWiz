using MultiWiz.Core.Hotkeys;

namespace MultiWiz.Core.Platform;

/// <summary>System-wide hotkeys (RegisterHotKey).</summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>
    /// Registers a global hotkey. Returns null if the binding is invalid or already taken by another program.
    /// The callback runs on the platform message thread, which holds foreground-activation rights at that
    /// moment, so it may call <see cref="IWindowService.Focus"/> directly. Keep it short.
    /// Dispose the result to unregister.
    /// </summary>
    IDisposable? TryRegister(HotkeyBinding binding, Action callback);
}
