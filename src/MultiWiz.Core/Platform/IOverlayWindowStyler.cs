namespace MultiWiz.Core.Platform;

/// <summary>Applies Win32 extended styles to MultiWiz's own windows.</summary>
public interface IOverlayWindowStyler
{
    /// <summary>
    /// Makes a window a topmost tool window that never activates and is hidden from Alt-Tab.
    /// When <paramref name="clickThrough"/> is true, mouse input passes through to whatever is underneath.
    /// </summary>
    void ApplyOverlayStyle(nint hwnd, bool clickThrough);

    /// <summary>Sets or clears WS_EX_NOACTIVATE so clicking the window does not take focus from the game.</summary>
    void SetNoActivate(nint hwnd, bool noActivate);

    /// <summary>Places the window directly above <paramref name="insertAfter"/> in z-order without activating either.</summary>
    void PlaceAbove(nint hwnd, nint insertAfter);
}
