using MultiWiz.Core.Primitives;

namespace MultiWiz.Core.Platform;

/// <summary>Top-level window queries and manipulation. All methods are safe to call from any thread.</summary>
public interface IWindowService
{
    /// <summary>The main (visible, unowned, largest) top-level window of a process, or 0.</summary>
    nint FindMainWindow(int processId);

    bool IsWindowAlive(nint hwnd);
    int GetProcessId(nint hwnd);
    nint GetForegroundWindow();
    string GetTitle(nint hwnd);
    bool IsMinimized(nint hwnd);

    /// <summary>
    /// Brings the window to the foreground, restoring it if minimized. Uses the permitted techniques
    /// (direct SetForegroundWindow, then input-attach / ALT-key fallbacks). Returns true if it became foreground.
    /// </summary>
    bool Focus(nint hwnd);

    /// <summary>Visible frame bounds (DWM extended frame bounds, i.e. without invisible resize borders).</summary>
    PixelRect? GetBounds(nint hwnd);

    /// <summary>Client area in screen coordinates.</summary>
    PixelRect? GetClientBounds(nint hwnd);

    /// <summary>
    /// Moves (and if <paramref name="resize"/>, resizes) the window so its visible frame matches
    /// <paramref name="bounds"/>. Restores the window first if minimized/maximized. Does not activate it.
    /// </summary>
    bool SetBounds(nint hwnd, PixelRect bounds, bool resize);

    /// <summary>Removes (or restores) the caption and resize frame of a window.</summary>
    bool SetBorderless(nint hwnd, bool borderless);
}
