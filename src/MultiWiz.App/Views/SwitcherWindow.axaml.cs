using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MultiWiz.App.Interop;
using MultiWiz.App.ViewModels;
using MultiWiz.Core.Platform;
using CorePixelRect = MultiWiz.Core.Primitives.PixelRect;
using CorePixelSize = MultiWiz.Core.Primitives.PixelSize;

namespace MultiWiz.App.Views;

/// <summary>
/// The compact, always-on-top switcher. Window plumbing only: dragging, remembering the position, and keeping the
/// window from taking focus away from the game (WS_EX_NOACTIVATE + WM_MOUSEACTIVATE) when the setting is on, and placing
/// the live previews (native windows from <see cref="IThumbnailService"/>) over each entry's placeholder.
/// </summary>
public partial class SwitcherWindow : Window
{
    private readonly Win32Properties.CustomWindowStylesCallback _stylesCallback;
    private readonly Win32Properties.CustomWndProcHookCallback _wndProcHook;
    private const string PreviewHostClass = "thumb-host";

    private readonly Dictionary<nint, PreviewEntry> _previews = new();

    // A game window can change size without anything here moving, so re-check the previews while the window is shown.
    private readonly DispatcherTimer _previewCheck = new() { Interval = TimeSpan.FromSeconds(1) };
    private IOverlayWindowStyler? _styler;
    private IThumbnailService? _thumbnails;
    private SwitcherViewModel? _viewModel;
    private bool _noActivate = true;

    public SwitcherWindow()
    {
        InitializeComponent();

        // Avalonia rebuilds GWL_EXSTYLE when some window properties change; re-add our bits every time.
        _stylesCallback = (style, exStyle) =>
            (style, exStyle | Win32WindowStyles.WS_EX_TOOLWINDOW | (_noActivate ? Win32WindowStyles.WS_EX_NOACTIVATE : 0u));
        _wndProcHook = WndProcHook;
        Win32Properties.AddWindowStylesCallback(this, _stylesCallback);
        Win32Properties.AddWndProcHookCallback(this, _wndProcHook);

        PositionChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _viewModel?.RememberPosition(Position.X, Position.Y);
            }

            SyncPreviews();
        };
        LayoutUpdated += (_, _) => SyncPreviews();
        ScalingChanged += (_, _) => SyncPreviews();
        _previewCheck.Tick += (_, _) => SyncPreviews();
    }

    /// <summary>Connects the platform styler and places the window where the user left it.</summary>
    public void Attach(IOverlayWindowStyler styler, IThumbnailService thumbnails)
    {
        _styler = styler;
        _thumbnails = thumbnails;
        PlaceInitially();
        ApplyNoActivate();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as SwitcherViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _noActivate = _viewModel.DoNotStealFocus;
            ApplyNoActivate();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty)
        {
            return;
        }

        if (IsVisible)
        {
            _previewCheck.Start();
            Dispatcher.UIThread.Post(SyncPreviews, DispatcherPriority.Background);
        }
        else
        {
            _previewCheck.Stop();
            ReleasePreviews();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (e.CloseReason == WindowCloseReason.WindowClosing && !e.IsProgrammatic)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _previewCheck.Stop();
        ReleasePreviews();
        Win32Properties.RemoveWindowStylesCallback(this, _stylesCallback);
        Win32Properties.RemoveWndProcHookCallback(this, _wndProcHook);
        base.OnClosed(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SwitcherViewModel.DoNotStealFocus) && _viewModel is not null)
        {
            _noActivate = _viewModel.DoNotStealFocus;
            ApplyNoActivate();
        }
    }

    private void SyncPreviews()
    {
        if (_thumbnails is null || !IsVisible || TryGetPlatformHandle() is not { } handle)
        {
            ReleasePreviews();
            return;
        }

        var scale = RenderScaling;
        var shown = new HashSet<nint>();
        foreach (var host in this.GetVisualDescendants().OfType<Border>())
        {
            if (!host.Classes.Contains(PreviewHostClass)
                || !host.IsVisible
                || host.DataContext is not SwitcherEntryViewModel entry
                || !entry.ShowPreview
                || entry.WindowHandle == 0
                || host.TranslatePoint(new Point(0, 0), this) is not { } origin)
            {
                continue;
            }

            var destination = new CorePixelRect(
                (int)Math.Round(origin.X * scale),
                (int)Math.Round(origin.Y * scale),
                (int)Math.Round(host.Bounds.Width * scale),
                (int)Math.Round(host.Bounds.Height * scale));

            if (!_previews.TryGetValue(entry.WindowHandle, out var preview))
            {
                var thumbnail = _thumbnails.Create(
                    handle.Handle,
                    entry.WindowHandle,
                    () => Dispatcher.UIThread.Post(() => entry.FocusCommand.Execute(null)));
                if (thumbnail is null)
                {
                    continue;
                }

                preview = new PreviewEntry(thumbnail);
                _previews[entry.WindowHandle] = preview;
            }

            var sourceSize = preview.Thumbnail.SourceSize;
            if (preview.LastDestination != destination || preview.LastSourceSize != sourceSize
                || !Equals(preview.LastWindowPosition, Position))
            {
                preview.Thumbnail.Update(destination, visible: !destination.IsEmpty);
                preview.LastDestination = destination;
                preview.LastSourceSize = sourceSize;
                preview.LastWindowPosition = Position;
            }

            shown.Add(entry.WindowHandle);
        }

        foreach (var source in _previews.Keys.Where(source => !shown.Contains(source)).ToArray())
        {
            _previews[source].Thumbnail.Dispose();
            _previews.Remove(source);
        }
    }

    private void ReleasePreviews()
    {
        foreach (var preview in _previews.Values)
        {
            preview.Thumbnail.Dispose();
        }

        _previews.Clear();
    }

    private void OnDragAreaPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnHideClick(object? sender, RoutedEventArgs e) => Hide();

    private void ApplyNoActivate()
    {
        if (_styler is not null && TryGetPlatformHandle() is { } handle)
        {
            _styler.SetNoActivate(handle.Handle, _noActivate);
        }
    }

    private void PlaceInitially()
    {
        if (_viewModel?.SavedPosition is { } saved
            && Screens.ScreenFromPoint(new PixelPoint(saved.X + 24, saved.Y + 24)) is not null)
        {
            Position = new PixelPoint(saved.X, saved.Y);
            return;
        }

        // Default: right edge of the primary screen, a third of the way down.
        if (Screens.Primary is { } primary)
        {
            var area = primary.WorkingArea;
            var widthPixels = (int)Math.Ceiling(Width * primary.Scaling);
            var edgeGap = (int)Math.Round(16 * primary.Scaling);
            Position = new PixelPoint(area.Right - widthPixels - edgeGap, area.Y + area.Height / 3);
        }
    }

    private IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_noActivate && msg == Win32WindowStyles.WM_MOUSEACTIVATE)
        {
            // Let the click through to the button without activating the window.
            handled = true;
            return Win32WindowStyles.MA_NOACTIVATE;
        }

        return IntPtr.Zero;
    }

    private sealed class PreviewEntry(IWindowThumbnail thumbnail)
    {
        public IWindowThumbnail Thumbnail { get; } = thumbnail;

        public CorePixelRect? LastDestination { get; set; }

        public CorePixelSize LastSourceSize { get; set; }

        public PixelPoint? LastWindowPosition { get; set; }
    }
}
