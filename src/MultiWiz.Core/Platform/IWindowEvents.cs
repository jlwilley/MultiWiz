using MultiWiz.Core.Primitives;

namespace MultiWiz.Core.Platform;

public sealed record ForegroundChangedEventArgs(nint WindowHandle, int ProcessId);

/// <summary>State of a tracked window. <see cref="ClientBounds"/> is null when the window is closed or minimized.</summary>
public readonly record struct WindowTrackingUpdate(PixelRect? ClientBounds, bool IsMinimized, bool IsForeground, bool IsClosed);

/// <summary>System window events (WinEvent hooks).</summary>
public interface IWindowEvents : IDisposable
{
    /// <summary>Raised when any window becomes the foreground window. Raised on the platform message thread; keep handlers short.</summary>
    event EventHandler<ForegroundChangedEventArgs>? ForegroundChanged;

    /// <summary>
    /// Reports moves, resizes, minimize/restore, foreground changes and closure of one window. The callback is
    /// invoked once immediately with the current state, then on changes (coalesced; at most ~60 per second),
    /// on the platform message thread. Dispose the result to stop tracking.
    /// </summary>
    IDisposable TrackWindow(nint hwnd, Action<WindowTrackingUpdate> onUpdate);
}
