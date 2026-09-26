using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Primitives;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MultiWiz.Platform.Windows.Windowing;

/// <summary>
/// Live DWM thumbnails of game windows.
/// </summary>
/// <remarks>
/// Avalonia draws its windows with DirectComposition, and DWM draws thumbnails registered on such a window underneath
/// that content, so a thumbnail registered on an Avalonia window is never visible. Each preview therefore gets its own
/// small, plain Win32 popup ("host") owned by the destination window and placed over the preview area. Owned popups
/// stay directly above their owner and never take focus; clicking one runs the preview's click action. Hosts are
/// created, moved and destroyed on the <see cref="Win32MessageThread"/>, which pumps their messages.
/// </remarks>
internal sealed unsafe class ThumbnailService : IThumbnailService, IDisposable
{
    private const uint WmMouseActivate = 0x0021;
    private const uint WmLButtonUp = 0x0202;
    private const int MaNoActivate = 3;
    private const int ColorWindowText = 8;

    private readonly Win32MessageThread _messageThread;
    private readonly ILogger<ThumbnailService> _logger;
    private readonly string _className = $"MultiWiz.Preview.{Guid.NewGuid():N}";

    // The window class keeps a native pointer to this delegate, so it must stay referenced.
    private readonly WNDPROC _hostProcedure;
    private readonly ConcurrentDictionary<nint, Action> _clickActions = new();
    private bool _classRegistered;

    public ThumbnailService(Win32MessageThread messageThread, ILogger<ThumbnailService> logger)
    {
        _messageThread = messageThread;
        _logger = logger;
        _hostProcedure = HostProcedure;
    }

    public IWindowThumbnail? Create(nint destinationWindow, nint sourceWindow, Action? onClick = null)
    {
        if (!NativeWindowHelpers.IsAlive(destinationWindow) || !NativeWindowHelpers.IsAlive(sourceWindow))
        {
            return null;
        }

        try
        {
            return _messageThread.Invoke(() => CreateOnMessageThread(destinationWindow, sourceWindow, onClick));
        }
        catch (Exception ex) when (ex is Win32Exception or ObjectDisposedException)
        {
            _logger.LogWarning(ex, "Could not create a live preview for window {Source:X}.", sourceWindow);
            return null;
        }
    }

    public void Dispose()
    {
        _clickActions.Clear();
    }

    private WindowThumbnail? CreateOnMessageThread(nint destinationWindow, nint sourceWindow, Action? onClick)
    {
        EnsureClassRegistered();

        using var instance = PInvoke.GetModuleHandle((string?)null);
        var host = PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE,
            _className,
            "MultiWiz preview",
            WINDOW_STYLE.WS_POPUP,
            0, 0, 0, 0,
            (HWND)destinationWindow,
            null,
            instance,
            null);
        if (host.IsNull)
        {
            _logger.LogWarning("Creating a preview host failed (error {Error}).", Marshal.GetLastPInvokeError());
            return null;
        }

        var result = PInvoke.DwmRegisterThumbnail(host, (HWND)sourceWindow, out nint thumbnailId);
        if (result.Failed)
        {
            _logger.LogWarning(
                "DwmRegisterThumbnail failed for window {Source:X} (HRESULT 0x{Result:X8}).", sourceWindow, result.Value);
            PInvoke.DestroyWindow(host);
            return null;
        }

        if (onClick is not null)
        {
            _clickActions[(nint)host] = onClick;
        }

        return new WindowThumbnail(this, (nint)host, destinationWindow, sourceWindow, thumbnailId);
    }

    private void EnsureClassRegistered()
    {
        if (_classRegistered)
        {
            return;
        }

        using var instance = PInvoke.GetModuleHandle((string?)null);
        fixed (char* className = _className)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _hostProcedure,
                hInstance = (HINSTANCE)instance.DangerousGetHandle(),
                lpszClassName = className,
                // System color brushes are passed as (color index + 1); the letterbox bars are drawn in it.
                hbrBackground = (HBRUSH)(nint)(ColorWindowText + 1),
            };

            if (PInvoke.RegisterClassEx(in windowClass) == 0)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }
        }

        _classRegistered = true;
    }

    private LRESULT HostProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        // Exceptions must never unwind into native code.
        try
        {
            switch (message)
            {
                case WmMouseActivate:
                    // Clicking a preview must not activate it (or its owner); the click switches to the game instead.
                    return new LRESULT(MaNoActivate);
                case WmLButtonUp:
                    if (_clickActions.TryGetValue((nint)window, out var action))
                    {
                        action();
                    }

                    return new LRESULT(0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A preview click handler failed.");
        }

        return PInvoke.DefWindowProc(window, message, wParam, lParam);
    }

    private void DestroyHost(nint host, nint thumbnailId)
    {
        _clickActions.TryRemove(host, out _);
        void Destroy()
        {
            PInvoke.DwmUnregisterThumbnail(thumbnailId);
            PInvoke.DestroyWindow((HWND)host);
        }

        try
        {
            _messageThread.Post(Destroy);
        }
        catch (ObjectDisposedException)
        {
            // The message thread is gone, and its windows with it.
        }
    }

    private void MoveHost(nint host, nint destinationWindow, nint sourceWindow, nint thumbnailId, PixelRect destination, bool visible, byte opacity)
    {
        if (!NativeWindowHelpers.IsAlive(host) || !NativeWindowHelpers.IsAlive(destinationWindow))
        {
            return;
        }

        // The preview area is relative to the owner's client area; the host is a top-level window in screen coordinates.
        var origin = new System.Drawing.Point(0, 0);
        if (!PInvoke.ClientToScreen((HWND)destinationWindow, ref origin))
        {
            return;
        }

        var show = visible && !destination.IsEmpty;
        var flags = SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER |
                    (show ? SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW : SET_WINDOW_POS_FLAGS.SWP_HIDEWINDOW);
        PInvoke.SetWindowPos(
            (HWND)host, HWND.Null,
            origin.X + destination.X, origin.Y + destination.Y,
            Math.Max(1, destination.Width), Math.Max(1, destination.Height),
            flags);

        if (!show)
        {
            return;
        }

        var target = Letterbox(new PixelRect(0, 0, destination.Width, destination.Height), GetSourceSize(sourceWindow));
        var properties = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = PInvoke.DWM_TNP_RECTDESTINATION | PInvoke.DWM_TNP_VISIBLE | PInvoke.DWM_TNP_OPACITY |
                      PInvoke.DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new RECT(target.X, target.Y, target.Right, target.Bottom),
            opacity = opacity,
            fVisible = !target.IsEmpty,
            fSourceClientAreaOnly = true,
        };

        var result = PInvoke.DwmUpdateThumbnailProperties(thumbnailId, in properties);
        if (result.Failed)
        {
            _logger.LogDebug(
                "DwmUpdateThumbnailProperties failed for window {Source:X} (HRESULT 0x{Result:X8}).", sourceWindow, result.Value);
        }
    }

    /// <summary>
    /// The source's client-area size, which is exactly what the thumbnail shows (<c>DWM_TNP_SOURCECLIENTAREAONLY</c>),
    /// so letterboxing preserves the game's real aspect ratio.
    /// </summary>
    private static PixelSize GetSourceSize(nint sourceWindow) =>
        NativeWindowHelpers.IsAlive(sourceWindow) && NativeWindowHelpers.GetClientBounds((HWND)sourceWindow) is { } client
            ? new PixelSize(client.Width, client.Height)
            : default;

    private static PixelRect Letterbox(PixelRect destination, PixelSize source)
    {
        if (destination.IsEmpty || source.Width <= 0 || source.Height <= 0)
        {
            return destination;
        }

        var scale = Math.Min((double)destination.Width / source.Width, (double)destination.Height / source.Height);
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var x = destination.X + ((destination.Width - width) / 2);
        var y = destination.Y + ((destination.Height - height) / 2);
        return new PixelRect(x, y, width, height);
    }

    private sealed class WindowThumbnail(
        ThumbnailService owner, nint host, nint destinationWindow, nint sourceWindow, nint thumbnailId) : IWindowThumbnail
    {
        private int _disposed;

        public PixelSize SourceSize => GetSourceSize(sourceWindow);

        public void Update(PixelRect destination, bool visible, byte opacity = 255)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            try
            {
                owner._messageThread.Post(() =>
                {
                    if (Volatile.Read(ref _disposed) == 0)
                    {
                        owner.MoveHost(host, destinationWindow, sourceWindow, thumbnailId, destination, visible, opacity);
                    }
                });
            }
            catch (ObjectDisposedException)
            {
                // Shutting down.
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.DestroyHost(host, thumbnailId);
            }
        }
    }
}
