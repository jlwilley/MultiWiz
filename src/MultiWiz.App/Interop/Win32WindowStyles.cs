namespace MultiWiz.App.Interop;

/// <summary>
/// Win32 constants used by the style callbacks and WndProc hooks that Avalonia exposes through
/// <c>Win32Properties</c>. The actual style changes go through <c>IOverlayWindowStyler</c>.
/// </summary>
internal static class Win32WindowStyles
{
    public const uint WS_EX_TOPMOST = 0x0000_0008;
    public const uint WS_EX_TRANSPARENT = 0x0000_0020;
    public const uint WS_EX_TOOLWINDOW = 0x0000_0080;
    public const uint WS_EX_LAYERED = 0x0008_0000;
    public const uint WS_EX_NOACTIVATE = 0x0800_0000;

    /// <summary>Click-through, topmost tool window that never activates.</summary>
    public const uint OverlayExStyles = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;

    public const uint WM_MOUSEACTIVATE = 0x0021;
    public const int MA_NOACTIVATE = 3;
}
