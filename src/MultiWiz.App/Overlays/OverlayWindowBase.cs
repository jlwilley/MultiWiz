using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MultiWiz.App.Interop;
using MultiWiz.Core.Platform;
using CorePixelRect = MultiWiz.Core.Primitives.PixelRect;

namespace MultiWiz.App.Overlays;

/// <summary>
/// Base for windows drawn over a game client: transparent, borderless, topmost, never activated, hidden from
/// Alt-Tab and click-through. It follows the game window's client area (physical pixels) through
/// <see cref="IWindowEvents.TrackWindow"/>, hides while the game is minimized, rises above the other overlays when
/// its game comes to the front, and reports when the game window closes. Future overlays (live stats, damage
/// planner) derive from this and only supply content and placement.
/// </summary>
public class OverlayWindowBase : Window
{
    // Avalonia recomputes GWL_EXSTYLE from scratch when some window properties change; this re-adds the overlay bits.
    private static readonly Win32Properties.CustomWindowStylesCallback OverlayStylesCallback =
        static (style, exStyle) => (style, exStyle | Win32WindowStyles.OverlayExStyles);

    private readonly Lock _updateLock = new();
    private WindowTrackingUpdate? _pendingUpdate;
    private int _updateQueued;
    private IOverlayWindowStyler? _styler;
    private IDisposable? _tracking;
    private CorePixelRect? _targetClientBounds;
    private bool _targetHidden = true;
    private bool _targetForeground;
    private bool _suppressed;
    private bool _occluded;
    private bool _hasBeenShown;
    private bool _closed;

    public OverlayWindowBase()
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        Focusable = false;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Win32Properties.AddWindowStylesCallback(this, OverlayStylesCallback);

        // Showing the window can make Avalonia rewrite its extended styles; make sure ours (and the layered alpha) stick.
        Opened += (_, _) => ApplyOverlayStyle();
    }

    /// <summary>The game window this overlay follows.</summary>
    public nint TargetWindow { get; private set; }

    /// <summary>True once the window has been closed (by the manager or by app shutdown).</summary>
    public bool IsClosed => _closed;

    /// <summary>The target's client area in physical pixels; null while it is minimized, closed or not yet known.</summary>
    public CorePixelRect? TargetClientBounds
    {
        get
        {
            if (_targetHidden || _targetClientBounds is not { } bounds || bounds.IsEmpty)
            {
                return null;
            }

            return bounds;
        }
    }

    /// <summary>
    /// Where this overlay is drawn while visible, in physical pixels; null while the target has no visible client
    /// area. Before the overlay has been shown once its size is unknown, so only its top-left pixel is reported.
    /// </summary>
    public CorePixelRect? OverlayBounds
    {
        get
        {
            if (TargetClientBounds is not { } bounds)
            {
                return null;
            }

            var origin = GetPlacement(bounds);
            if (!_hasBeenShown)
            {
                return new CorePixelRect(origin.X, origin.Y, 1, 1);
            }

            var scale = RenderScaling;
            return new CorePixelRect(
                origin.X,
                origin.Y,
                Math.Max(1, (int)Math.Ceiling(ClientSize.Width * scale)),
                Math.Max(1, (int)Math.Ceiling(ClientSize.Height * scale)));
        }
    }

    /// <summary>Raised on the UI thread when the target window has been destroyed.</summary>
    public event EventHandler? TargetClosed;

    /// <summary>
    /// Raised on the UI thread when the target moved, resized, was minimized or restored, or changed foreground
    /// state, before the overlay is repositioned (so handlers can call <see cref="SetOccluded"/> first).
    /// </summary>
    public event EventHandler? TargetChanged;

    /// <summary>Applies the click-through overlay styles and starts following <paramref name="targetWindow"/>. UI thread.</summary>
    public void Attach(IOverlayWindowStyler styler, IWindowEvents windowEvents, nint targetWindow)
    {
        ArgumentNullException.ThrowIfNull(styler);
        ArgumentNullException.ThrowIfNull(windowEvents);
        if (_tracking is not null)
        {
            throw new InvalidOperationException("The overlay is already attached to a window.");
        }

        _styler = styler;
        TargetWindow = targetWindow;
        ApplyOverlayStyle();
        _tracking = windowEvents.TrackWindow(targetWindow, OnTrackingUpdate);
    }

    /// <summary>Hides the overlay regardless of the target's state (e.g. while a non-game app is in front). UI thread.</summary>
    public void SetSuppressed(bool suppressed)
    {
        if (_suppressed == suppressed)
        {
            return;
        }

        _suppressed = suppressed;
        UpdatePlacement(raise: false);
    }

    /// <summary>Hides the overlay while another window covers the spot where it would be drawn. UI thread.</summary>
    public void SetOccluded(bool occluded)
    {
        if (_occluded == occluded)
        {
            return;
        }

        _occluded = occluded;
        UpdatePlacement(raise: false);
    }

    /// <summary>Where the overlay's top-left corner goes, in physical pixels. Defaults to just inside the client area's top-left.</summary>
    protected virtual PixelPoint GetPlacement(CorePixelRect targetClientBounds)
    {
        var margin = (int)Math.Round(8 * DesktopScaling);
        return new PixelPoint(targetClientBounds.X + margin, targetClientBounds.Y + margin);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _tracking?.Dispose();
        _tracking = null;
        Win32Properties.RemoveWindowStylesCallback(this, OverlayStylesCallback);
        base.OnClosed(e);
    }

    // Platform message thread: keep only the latest update and hop to the UI thread once.
    private void OnTrackingUpdate(WindowTrackingUpdate update)
    {
        lock (_updateLock)
        {
            _pendingUpdate = update;
        }

        if (Interlocked.Exchange(ref _updateQueued, 1) == 0)
        {
            Dispatcher.UIThread.Post(ApplyPendingUpdate);
        }
    }

    private void ApplyPendingUpdate()
    {
        Volatile.Write(ref _updateQueued, 0);
        WindowTrackingUpdate? pending;
        lock (_updateLock)
        {
            pending = _pendingUpdate;
            _pendingUpdate = null;
        }

        if (_closed || pending is not { } update)
        {
            return;
        }

        if (update.IsClosed)
        {
            _targetHidden = true;
            _targetForeground = false;
            UpdatePlacement(raise: false);
            TargetClosed?.Invoke(this, EventArgs.Empty);
            return;
        }

        var becameForeground = update.IsForeground && !_targetForeground;
        _targetForeground = update.IsForeground;
        _targetClientBounds = update.ClientBounds;
        _targetHidden = update.IsMinimized || update.ClientBounds is null;
        TargetChanged?.Invoke(this, EventArgs.Empty);

        // Every overlay shares the topmost band, so the one over the game in front goes to the top of it; otherwise a
        // badge shown or raised later (often for another client at the same spot) would cover it.
        UpdatePlacement(raise: becameForeground);
    }

    private void UpdatePlacement(bool raise)
    {
        if (_closed)
        {
            return;
        }

        if (_suppressed || _occluded || TargetClientBounds is not { } bounds)
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        Position = GetPlacement(bounds);
        if (!IsVisible)
        {
            Show();
            _hasBeenShown = true;

            // Showing without activation keeps the old z-order slot, which may be below other overlays.
            raise |= _targetForeground;
        }

        if (raise)
        {
            ApplyOverlayStyle();
        }
    }

    // SetWindowPos(HWND_TOPMOST) inside the styler also moves the window to the top of the topmost band.
    private void ApplyOverlayStyle()
    {
        if (_styler is not null && TryGetPlatformHandle() is { } handle)
        {
            _styler.ApplyOverlayStyle(handle.Handle, clickThrough: true);
        }
    }
}
