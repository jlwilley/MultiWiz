using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Primitives;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MultiWiz.Platform.Windows.Windowing;

/// <summary>Top-level window queries and manipulation. Every method works from any thread.</summary>
internal sealed unsafe class WindowService : IWindowService
{
    private const string WizardClientClass = "Wizard Graphical Client";
    private const string PirateClientClass = "Pirate Graphical Client";

    private static readonly nint FrameStyles = (nint)(uint)(
        WINDOW_STYLE.WS_CAPTION | WINDOW_STYLE.WS_THICKFRAME | WINDOW_STYLE.WS_SYSMENU |
        WINDOW_STYLE.WS_MINIMIZEBOX | WINDOW_STYLE.WS_MAXIMIZEBOX);

    private static readonly nint MaximizedStyle = (nint)(uint)WINDOW_STYLE.WS_MAXIMIZE;

    private readonly ILogger<WindowService> _logger;

    // Original GWL_STYLE of windows made borderless, so the exact frame can be put back.
    private readonly ConcurrentDictionary<nint, nint> _originalStyles = new();

    public WindowService(ILogger<WindowService> logger)
    {
        _logger = logger;
    }

    public nint FindMainWindow(int processId)
    {
        if (processId <= 0)
        {
            return 0;
        }

        nint gameWindow = 0;
        nint largestWindow = 0;
        long largestArea = -1;

        PInvoke.EnumWindows((hwnd, _) =>
        {
            PInvoke.GetWindowThreadProcessId(hwnd, out uint ownerProcessId);
            if (ownerProcessId != (uint)processId || !PInvoke.IsWindowVisible(hwnd)
                || !PInvoke.GetWindow(hwnd, GET_WINDOW_CMD.GW_OWNER).IsNull)
            {
                return true;
            }

            if (IsGameClientWindow(hwnd))
            {
                gameWindow = hwnd;
                return false; // Stop enumerating: this is the one.
            }

            if (PInvoke.GetWindowRect(hwnd, out RECT rect))
            {
                var area = (long)(rect.right - rect.left) * (rect.bottom - rect.top);
                if (area > largestArea)
                {
                    largestArea = area;
                    largestWindow = hwnd;
                }
            }

            return true;
        }, default);

        return gameWindow != 0 ? gameWindow : largestWindow;
    }

    public bool IsWindowAlive(nint hwnd) => NativeWindowHelpers.IsAlive(hwnd);

    public int GetProcessId(nint hwnd)
    {
        if (hwnd == 0)
        {
            return 0;
        }

        PInvoke.GetWindowThreadProcessId((HWND)hwnd, out uint processId);
        return (int)processId;
    }

    public nint GetForegroundWindow() => PInvoke.GetForegroundWindow();

    public string GetTitle(nint hwnd)
    {
        if (hwnd == 0)
        {
            return string.Empty;
        }

        var window = (HWND)hwnd;
        var length = PInvoke.GetWindowTextLength(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        Span<char> buffer = length < 512 ? stackalloc char[length + 1] : new char[length + 1];
        var copied = PInvoke.GetWindowText(window, buffer);
        return copied <= 0 ? string.Empty : new string(buffer[..copied]);
    }

    public bool IsMinimized(nint hwnd) => hwnd != 0 && PInvoke.IsIconic((HWND)hwnd);

    public bool Focus(nint hwnd)
    {
        if (!NativeWindowHelpers.IsAlive(hwnd))
        {
            return false;
        }

        var target = (HWND)hwnd;
        if (PInvoke.IsIconic(target))
        {
            // ShowWindow on another thread's window waits for that thread; a loading or hung client would block the
            // caller (the message thread that runs every hotkey), so the restore is posted instead.
            PInvoke.ShowWindowAsync(target, SHOW_WINDOW_CMD.SW_RESTORE);
        }

        if (IsForeground(target) || PInvoke.SetForegroundWindow(target))
        {
            return true;
        }

        // Windows only lets the process that received the last input event take the foreground.
        // A synthesized ALT tap counts as input, which usually unlocks SetForegroundWindow.
        TapAltKey();
        if (PInvoke.SetForegroundWindow(target))
        {
            return true;
        }

        if (ForceForegroundWithAttachedInput(target))
        {
            return true;
        }

        _logger.LogDebug("Could not bring window {Window:X} to the foreground.", hwnd);
        return false;
    }

    public PixelRect? GetBounds(nint hwnd) =>
        NativeWindowHelpers.IsAlive(hwnd) ? NativeWindowHelpers.GetFrameBounds((HWND)hwnd) : null;

    public PixelRect? GetClientBounds(nint hwnd) =>
        NativeWindowHelpers.IsAlive(hwnd) ? NativeWindowHelpers.GetClientBounds((HWND)hwnd) : null;

    public bool SetBounds(nint hwnd, PixelRect bounds, bool resize)
    {
        if (!NativeWindowHelpers.IsAlive(hwnd))
        {
            return false;
        }

        var window = (HWND)hwnd;
        if (IsHung(window))
        {
            return false;
        }

        // Synchronous, so the border measurement below sees the restored rectangle.
        if (PInvoke.IsIconic(window) || IsMaximized(window))
        {
            PInvoke.ShowWindow(window, SHOW_WINDOW_CMD.SW_SHOWNOACTIVATE);
        }

        if (!PInvoke.GetWindowRect(window, out RECT windowRect))
        {
            return false;
        }

        // The window rectangle includes invisible resize borders; offset by them so the visible frame lands on 'bounds'.
        var frame = NativeWindowHelpers.TryGetExtendedFrame(window, out var extendedFrame) ? extendedFrame : windowRect;
        var borderLeft = frame.left - windowRect.left;
        var borderTop = frame.top - windowRect.top;
        var borderRight = windowRect.right - frame.right;
        var borderBottom = windowRect.bottom - frame.bottom;

        // Async so a busy game does not block the caller.
        var flags = SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE |
                    SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS;
        if (!resize)
        {
            flags |= SET_WINDOW_POS_FLAGS.SWP_NOSIZE;
        }

        var width = Math.Max(1, bounds.Width + borderLeft + borderRight);
        var height = Math.Max(1, bounds.Height + borderTop + borderBottom);
        if (PInvoke.SetWindowPos(window, HWND.Null, bounds.X - borderLeft, bounds.Y - borderTop, width, height, flags))
        {
            return true;
        }

        _logger.LogDebug("SetWindowPos failed for window {Window:X}.", hwnd);
        return false;
    }

    public bool SetBorderless(nint hwnd, bool borderless)
    {
        ForgetClosedWindows();
        if (!NativeWindowHelpers.IsAlive(hwnd))
        {
            return false;
        }

        var window = (HWND)hwnd;
        if (IsHung(window))
        {
            return false;
        }

        var style = PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        if (style == 0)
        {
            return false;
        }

        nint newStyle;
        if (borderless)
        {
            _originalStyles.TryAdd(hwnd, style);
            newStyle = style & ~FrameStyles;
        }
        else
        {
            newStyle = _originalStyles.TryRemove(hwnd, out var original)
                ? (style & ~FrameStyles) | (original & FrameStyles)
                : style | FrameStyles;
        }

        if (newStyle != style)
        {
            PInvoke.SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_STYLE, newStyle);
        }

        // SWP_FRAMECHANGED makes Windows recompute the non-client area for the new style.
        return PInvoke.SetWindowPos(window, HWND.Null, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER |
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
    }

    private static bool IsGameClientWindow(HWND hwnd)
    {
        const int capacity = 64;
        char* buffer = stackalloc char[capacity];
        var length = PInvoke.GetClassName(hwnd, buffer, capacity);
        if (length <= 0)
        {
            return false;
        }

        var className = new ReadOnlySpan<char>(buffer, length);
        return MemoryExtensions.Equals(className, WizardClientClass, StringComparison.Ordinal)
            || MemoryExtensions.Equals(className, PirateClientClass, StringComparison.Ordinal);
    }

    // Changing the style or the position of another thread's window waits for that thread to handle the messages.
    private bool IsHung(HWND hwnd)
    {
        if (!PInvoke.IsHungAppWindow(hwnd))
        {
            return false;
        }

        _logger.LogDebug("Window {Window:X} is not responding; leaving it unchanged.", (nint)hwnd);
        return true;
    }

    private static bool IsForeground(HWND hwnd) => (nint)PInvoke.GetForegroundWindow() == (nint)hwnd;

    private static bool IsMaximized(HWND hwnd) =>
        (PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE) & MaximizedStyle) != 0;

    private static void TapAltKey()
    {
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_TYPE.INPUT_KEYBOARD;
        inputs[0].ki.wVk = VIRTUAL_KEY.VK_MENU;
        inputs[1].type = INPUT_TYPE.INPUT_KEYBOARD;
        inputs[1].ki.wVk = VIRTUAL_KEY.VK_MENU;
        inputs[1].ki.dwFlags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        PInvoke.SendInput(inputs, sizeof(INPUT));
    }

    private static bool ForceForegroundWithAttachedInput(HWND target)
    {
        var foreground = PInvoke.GetForegroundWindow();
        uint foregroundThread = foreground.IsNull ? 0 : PInvoke.GetWindowThreadProcessId(foreground);
        var currentThread = PInvoke.GetCurrentThreadId();

        // Sharing the foreground thread's input state lets this thread change the foreground window.
        var attached = foregroundThread != 0 && foregroundThread != currentThread
            && PInvoke.AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            // BringWindowToTop without the wait: a synchronous z-order change blocks on a busy or hung client.
            PInvoke.SetWindowPos(target, HWND.Null, 0, 0, 0, 0,
                SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_ASYNCWINDOWPOS);
            PInvoke.SetForegroundWindow(target);
        }
        finally
        {
            if (attached)
            {
                PInvoke.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        return IsForeground(target);
    }

    private void ForgetClosedWindows()
    {
        foreach (var hwnd in _originalStyles.Keys)
        {
            if (!NativeWindowHelpers.IsAlive(hwnd))
            {
                _originalStyles.TryRemove(hwnd, out _);
            }
        }
    }
}
