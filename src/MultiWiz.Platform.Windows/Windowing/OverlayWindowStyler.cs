using MultiWiz.Core.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MultiWiz.Platform.Windows.Windowing;

/// <summary>Extended window styles for MultiWiz's own overlay and switcher windows.</summary>
internal sealed class OverlayWindowStyler : IOverlayWindowStyler
{
    private static readonly nint OverlayStyles = (nint)(uint)(
        WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE | WINDOW_EX_STYLE.WS_EX_TOPMOST);

    private static readonly nint ClickThroughStyles = (nint)(uint)(
        WINDOW_EX_STYLE.WS_EX_LAYERED | WINDOW_EX_STYLE.WS_EX_TRANSPARENT);

    private static readonly nint TransparentStyle = (nint)(uint)WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
    private static readonly nint NoActivateStyle = (nint)(uint)WINDOW_EX_STYLE.WS_EX_NOACTIVATE;

    public void ApplyOverlayStyle(nint hwnd, bool clickThrough)
    {
        if (!NativeWindowHelpers.IsAlive(hwnd))
        {
            return;
        }

        var window = (HWND)hwnd;
        var current = PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var updated = current | OverlayStyles;
        updated = clickThrough ? updated | ClickThroughStyles : updated & ~TransparentStyle;
        if (updated != current)
        {
            PInvoke.SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, updated);
        }

        if (clickThrough)
        {
            // A layered window without attributes is not drawn; full alpha keeps composition-rendered content visible.
            PInvoke.SetLayeredWindowAttributes(window, default(COLORREF), 255, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);
        }

        // WS_EX_TOPMOST only takes effect through SetWindowPos.
        PInvoke.SetWindowPos(window, HWND.HWND_TOPMOST, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
            SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
    }

    public void SetNoActivate(nint hwnd, bool noActivate)
    {
        if (!NativeWindowHelpers.IsAlive(hwnd))
        {
            return;
        }

        var window = (HWND)hwnd;
        var current = PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var updated = noActivate ? current | NoActivateStyle : current & ~NoActivateStyle;
        if (updated == current)
        {
            return;
        }

        PInvoke.SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, updated);
        PInvoke.SetWindowPos(window, HWND.Null, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER |
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
    }

    public void PlaceAbove(nint hwnd, nint insertAfter)
    {
        if (!NativeWindowHelpers.IsAlive(hwnd) || !NativeWindowHelpers.IsAlive(insertAfter) || hwnd == insertAfter)
        {
            return;
        }

        // SetWindowPos puts a window *below* hWndInsertAfter, so insert after the window currently above the target.
        var above = PInvoke.GetWindow((HWND)insertAfter, GET_WINDOW_CMD.GW_HWNDPREV);
        if ((nint)above == hwnd)
        {
            return;
        }

        // A null handle (HWND_TOP) means the target is already at the top of its z-order band.
        PInvoke.SetWindowPos((HWND)hwnd, above, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }
}
