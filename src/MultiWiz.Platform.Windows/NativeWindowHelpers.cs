using MultiWiz.Core.Primitives;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace MultiWiz.Platform.Windows;

/// <summary>Small window-geometry helpers shared by the windowing services. Safe to call from any thread.</summary>
internal static unsafe class NativeWindowHelpers
{
    public static bool IsAlive(nint hwnd) => hwnd != 0 && PInvoke.IsWindow((HWND)hwnd);

    public static PixelRect ToPixelRect(RECT rect) =>
        new(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);

    /// <summary>Visible frame (DWM extended frame bounds), falling back to the window rectangle.</summary>
    public static PixelRect? GetFrameBounds(HWND hwnd)
    {
        if (TryGetExtendedFrame(hwnd, out var frame))
        {
            return ToPixelRect(frame);
        }

        if (PInvoke.GetWindowRect(hwnd, out RECT window))
        {
            return ToPixelRect(window);
        }

        return null;
    }

    public static bool TryGetExtendedFrame(HWND hwnd, out RECT frame)
    {
        RECT bounds = default;
        var result = PInvoke.DwmGetWindowAttribute(
            hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &bounds, (uint)sizeof(RECT));
        frame = bounds;
        return result.Succeeded;
    }

    /// <summary>Client area in physical screen coordinates.</summary>
    public static PixelRect? GetClientBounds(HWND hwnd)
    {
        if (!PInvoke.GetClientRect(hwnd, out RECT client))
        {
            return null;
        }

        // GetClientRect reports the size in the window's own coordinates, which are logical for a DPI-unaware client
        // on a scaled monitor. Mapping both corners through ClientToScreen gives this per-monitor-aware process's
        // physical pixels either way.
        var topLeft = new System.Drawing.Point(client.left, client.top);
        if (!PInvoke.ClientToScreen(hwnd, ref topLeft))
        {
            return null;
        }

        var bottomRight = new System.Drawing.Point(client.right, client.bottom);
        if (!PInvoke.ClientToScreen(hwnd, ref bottomRight))
        {
            return null;
        }

        return new PixelRect(topLeft.X, topLeft.Y, bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y);
    }
}
