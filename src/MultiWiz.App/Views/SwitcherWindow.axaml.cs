using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MultiWiz.App.Interop;
using MultiWiz.App.ViewModels;
using MultiWiz.Core.Platform;

namespace MultiWiz.App.Views;

/// <summary>
/// The compact, always-on-top switcher. Window plumbing only: dragging, remembering the position, and keeping the
/// window from taking focus away from the game (WS_EX_NOACTIVATE + WM_MOUSEACTIVATE) when the setting is on.
/// </summary>
public partial class SwitcherWindow : Window
{
    private readonly Win32Properties.CustomWindowStylesCallback _stylesCallback;
    private readonly Win32Properties.CustomWndProcHookCallback _wndProcHook;
    private IOverlayWindowStyler? _styler;
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
        };
    }

    /// <summary>Connects the platform styler and places the window where the user left it.</summary>
    public void Attach(IOverlayWindowStyler styler)
    {
        _styler = styler;
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
}
