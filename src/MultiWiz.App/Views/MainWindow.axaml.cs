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

    private WindowPlacementTracker? _placement;
    private WindowState _restoreState = WindowState.Normal;

    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>Restores the last size/position (if still on a connected screen) and remembers future ones.</summary>
    public void Attach(WindowPlacementStore placements)
    {
        _placement = new WindowPlacementTracker(this, placements, PlacementKey);
        _placement.Restore();
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
            // Save now: quitting from the tray later closes the window while it is hidden.
            _placement?.Save();
            Hide();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _placement?.Save();

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
}
