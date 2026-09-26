using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MultiWiz.App.Services;

/// <summary>
/// Restores a window's saved size and position, and follows its normal (not minimized or maximized) bounds while the
/// user moves and resizes it. <see cref="Save"/> therefore stores the right placement at any moment: while the window
/// is hidden in the tray or minimized, and when it is maximized on another monitor than the one it was saved on.
/// UI thread.
/// </summary>
public sealed class WindowPlacementTracker
{
    private readonly Window _window;
    private readonly WindowPlacementStore _store;
    private readonly string _key;
    private WindowPlacement? _normal;
    private WindowState _stateBeforeMinimize = WindowState.Normal;
    private bool _checkQueued;

    public WindowPlacementTracker(Window window, WindowPlacementStore store, string key)
    {
        _window = window;
        _store = store;
        _key = key;
        window.Opened += (_, _) => QueueCheck();
        window.PositionChanged += (_, _) => QueueCheck();
        window.PropertyChanged += OnWindowPropertyChanged;
    }

    /// <summary>Moves the window to its saved placement when that is still on a connected screen. Call before showing it.</summary>
    public void Restore()
    {
        if (_store.Get(_key) is not { } placement
            || _window.Screens.ScreenFromPoint(new PixelPoint(placement.X + 48, placement.Y + 16)) is null)
        {
            return;
        }

        _normal = placement with { IsMaximized = false };
        _window.WindowStartupLocation = WindowStartupLocation.Manual;
        _window.Position = new PixelPoint(placement.X, placement.Y);
        _window.Width = Math.Max(_window.MinWidth, placement.Width);
        _window.Height = Math.Max(_window.MinHeight, placement.Height);
        if (placement.IsMaximized)
        {
            _stateBeforeMinimize = WindowState.Maximized;
            _window.WindowState = WindowState.Maximized;
        }
    }

    /// <summary>Stores the last normal bounds and whether the window is (or was, before minimizing) maximized.</summary>
    public void Save()
    {
        RememberNormalBounds();
        if (_normal is not { } normal)
        {
            return; // Never shown in a normal state; keep what is stored.
        }

        var maximized = _window.WindowState == WindowState.Maximized
            || (_window.WindowState == WindowState.Minimized && _stateBeforeMinimize == WindowState.Maximized);
        _store.Set(_key, normal with { IsMaximized = maximized });
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TopLevel.ClientSizeProperty)
        {
            QueueCheck();
        }
        else if (e.Property == Window.WindowStateProperty && _window.WindowState != WindowState.Minimized)
        {
            _stateBeforeMinimize = _window.WindowState;
            QueueCheck();
        }
    }

    // During a maximize or restore the position and size change before WindowState does, so look once it has settled.
    private void QueueCheck()
    {
        if (_checkQueued)
        {
            return;
        }

        _checkQueued = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                _checkQueued = false;
                RememberNormalBounds();
            },
            DispatcherPriority.Background);
    }

    private void RememberNormalBounds()
    {
        if (_window.IsVisible && _window.WindowState == WindowState.Normal)
        {
            _normal = new WindowPlacement(
                _window.Position.X, _window.Position.Y, _window.ClientSize.Width, _window.ClientSize.Height, IsMaximized: false);
        }
    }
}
