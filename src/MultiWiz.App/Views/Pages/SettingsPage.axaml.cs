using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MultiWiz.App.ViewModels;

namespace MultiWiz.App.Views.Pages;

/// <summary>
/// Settings page. The code-behind only forwards key presses to the hotkey row that is capturing, because the
/// capture box (a Button) would otherwise consume Enter/Space itself, and ends a capture when the user leaves it.
/// </summary>
public partial class SettingsPage : UserControl
{
    private Window? _hostWindow;

    public SettingsPage()
    {
        InitializeComponent();

        // Tunnel so the capturing row sees every key before the focused button handles it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(LostFocusEvent, (_, e) =>
        {
            if (IsCapturingRow(e.Source))
            {
                CancelCapture();
            }
        }, RoutingStrategies.Bubble);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // The capture pauses every global hotkey, and the focused capture box keeps focus while its window is
        // inactive or hidden, so leaving the window (Alt-Tab, clicking a game, closing to the tray) must end it.
        _hostWindow = TopLevel.GetTopLevel(this) as Window;
        if (_hostWindow is not null)
        {
            _hostWindow.Deactivated += OnHostWindowDeactivated;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_hostWindow is not null)
        {
            _hostWindow.Deactivated -= OnHostWindowDeactivated;
            _hostWindow = null;
        }

        CancelCapture();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnHostWindowDeactivated(object? sender, EventArgs e) => CancelCapture();

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is StyledElement { DataContext: HotkeyRowViewModel { IsCapturing: true } row })
        {
            e.Handled = row.HandleKey(e.Key, e.KeyModifiers);
        }
    }

    private static bool IsCapturingRow(object? source) =>
        source is StyledElement { DataContext: HotkeyRowViewModel { IsCapturing: true } };

    private void CancelCapture()
    {
        if (DataContext is SettingsPageViewModel viewModel)
        {
            viewModel.CancelCapture();
        }
    }
}
