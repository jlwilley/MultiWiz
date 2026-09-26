using Avalonia;
using Avalonia.Controls;
using MultiWiz.App.Services;
using MultiWiz.App.ViewModels;

namespace MultiWiz.App.Views;

/// <summary>
/// The main window. Code-behind only handles window plumbing: tray behaviour on minimize/close and remembering
/// the window's size and position.
/// </summary>
public partial class MainWindow : Window
{
    private const string PlacementKey = "main";

    private WindowPlacementStore? _placements;
    private WindowState _restoreState = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Restores the last size/position (if still on a connected screen) and remembers future ones.</summary>
    public void Attach(WindowPlacementStore placements)
    {
        _placements = placements;
        if (placements.Get(PlacementKey) is not { } placement || !IsOnScreen(placement))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(placement.X, placement.Y);
        Width = Math.Max(MinWidth, placement.Width);
        Height = Math.Max(MinHeight, placement.Height);
        if (placement.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    /// <summary>Shows the window if it is hidden in the tray, restores it if minimized, and activates it.</summary>
    public void BringToFront()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = _restoreState;
        }

        Activate();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != WindowStateProperty)
        {
            return;
        }

        if (WindowState != WindowState.Minimized)
        {
            _restoreState = WindowState;
        }
        else if (DataContext is MainWindowViewModel { MinimizeToTray: true })
        {
            Hide();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        SavePlacement();

        // Only the user closing the window is intercepted; app shutdown and programmatic closes go through.
        if (e.CloseReason == WindowCloseReason.WindowClosing && !e.IsProgrammatic
            && DataContext is MainWindowViewModel viewModel)
        {
            e.Cancel = true;
            if (viewModel.CloseToTray)
            {
                Hide();
            }
            else
            {
                viewModel.RequestQuit();
            }
        }

        base.OnClosing(e);
    }

    private void SavePlacement()
    {
        if (_placements is null || !IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            // Keep the last normal bounds so un-maximizing next time lands somewhere sensible.
            var previous = _placements.Get(PlacementKey);
            _placements.Set(PlacementKey, previous is null
                ? new WindowPlacement(Position.X, Position.Y, Width, Height, IsMaximized: true)
                : previous with { IsMaximized = true });
            return;
        }

        _placements.Set(
            PlacementKey,
            new WindowPlacement(Position.X, Position.Y, ClientSize.Width, ClientSize.Height, IsMaximized: false));
    }

    private bool IsOnScreen(WindowPlacement placement) =>
        Screens.ScreenFromPoint(new PixelPoint(placement.X + 48, placement.Y + 16)) is not null;
}
