using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiWiz.App.ViewModels;
using MultiWiz.App.Views;
using MultiWiz.Core.Hotkeys;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Settings;

namespace MultiWiz.App.Services;

/// <summary>
/// Owns the app's top-level windows (main window, switcher, Command Center) and the shutdown request.
/// Every member must be called on the UI thread.
/// </summary>
public sealed class WindowCoordinator
{
    /// <summary>
    /// How long after the Command Center lost activation it still counts as the window the user was looking at:
    /// clicking the main window's Command Center button (or the tray) takes the activation away just before the click.
    /// </summary>
    private const long RecentlyActiveMilliseconds = 700;

    private readonly IServiceProvider _services;
    private readonly IWindowService _windowService;
    private readonly ISettingsStore _settings;
    private readonly ILogger<WindowCoordinator> _logger;
    private MainWindow? _mainWindow;
    private SwitcherWindow? _switcherWindow;
    private CommandCenterWindow? _commandCenterWindow;
    private long? _commandCenterDeactivatedAt;

    public WindowCoordinator(
        IServiceProvider services,
        IWindowService windowService,
        ISettingsStore settings,
        ILogger<WindowCoordinator> logger)
    {
        _services = services;
        _windowService = windowService;
        _settings = settings;
        _logger = logger;
    }

    public bool IsShuttingDown { get; private set; }

    public MainWindow MainWindow => _mainWindow ??= CreateMainWindow();

    /// <summary>Shows, restores and activates the main window (from the tray, a hotkey or a second launch).</summary>
    public void ShowMainWindow()
    {
        if (IsShuttingDown)
        {
            return;
        }

        var window = MainWindow;
        window.BringToFront();

        // Activation from a background state can be refused by the foreground lock; the platform layer knows the
        // permitted fallbacks.
        if (!window.IsActive && window.TryGetPlatformHandle() is { } handle)
        {
            _windowService.Focus(handle.Handle);
        }
    }

    /// <summary>The owner for modal dialogs: the main window, made visible first if it was hidden in the tray.</summary>
    public Window GetDialogOwner()
    {
        var window = MainWindow;
        if (!window.IsVisible || window.WindowState == WindowState.Minimized)
        {
            window.BringToFront();
        }

        return window;
    }

    public void ToggleSwitcher()
    {
        if (_switcherWindow is { IsVisible: true })
        {
            _switcherWindow.Hide();
        }
        else
        {
            ShowSwitcher();
        }
    }

    public void ShowSwitcher()
    {
        if (IsShuttingDown)
        {
            return;
        }

        _switcherWindow ??= CreateSwitcherWindow();
        if (!_switcherWindow.IsVisible)
        {
            _switcherWindow.Show();
        }
    }

    public void ToggleCommandCenter()
    {
        // Hide it only when the user is looking at it. Minimized, or covered by the game the user just picked from a
        // tile (it is not topmost), counts as hidden: the toggle brings it back to the front instead.
        if (_commandCenterWindow is { IsVisible: true } visible && visible.WindowState != WindowState.Minimized
            && (visible.IsActive || Environment.TickCount64 - _commandCenterDeactivatedAt < RecentlyActiveMilliseconds))
        {
            visible.Hide();
            return;
        }

        if (IsShuttingDown)
        {
            return;
        }

        _commandCenterWindow ??= CreateCommandCenterWindow();
        if (_commandCenterWindow.WindowState == WindowState.Minimized)
        {
            _commandCenterWindow.WindowState = WindowState.Normal;
        }

        _commandCenterWindow.Show();
        _commandCenterWindow.Activate();

        // Same foreground-lock fallback as ShowMainWindow (the hotkey arrives while a game is in front).
        if (!_commandCenterWindow.IsActive && _commandCenterWindow.TryGetPlatformHandle() is { } handle)
        {
            _windowService.Focus(handle.Handle);
        }
    }

    public void ToggleNameBadges() =>
        _settings.Update(settings => settings with
        {
            Overlays = settings.Overlays with { ShowNameBadges = !settings.Overlays.ShowNameBadges },
        });

    /// <summary>Routes a hotkey that the UI handles (see <see cref="IHotkeyCoordinator.UiActionRequested"/>).</summary>
    public void HandleHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleSwitcher:
                ToggleSwitcher();
                break;
            case HotkeyAction.ToggleCommandCenter:
                ToggleCommandCenter();
                break;
            case HotkeyAction.ShowMainWindow:
                ShowMainWindow();
                break;
            case HotkeyAction.ToggleNameBadges:
                ToggleNameBadges();
                break;
            default:
                _logger.LogDebug("Hotkey action {Action} is not handled by the UI", action);
                break;
        }
    }

    /// <summary>Exits the app (the lifetime closes every window with <see cref="WindowCloseReason.ApplicationShutdown"/>).</summary>
    public void Quit()
    {
        if (IsShuttingDown)
        {
            return;
        }

        IsShuttingDown = true;
        _logger.LogInformation("Shutdown requested");
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private MainWindow CreateMainWindow()
    {
        var window = new MainWindow { DataContext = _services.GetRequiredService<MainWindowViewModel>() };
        window.Attach(_services.GetRequiredService<WindowPlacementStore>());
        return window;
    }

    private SwitcherWindow CreateSwitcherWindow()
    {
        var window = new SwitcherWindow { DataContext = _services.GetRequiredService<SwitcherViewModel>() };
        window.Attach(_services.GetRequiredService<IOverlayWindowStyler>());
        return window;
    }

    private CommandCenterWindow CreateCommandCenterWindow()
    {
        var window = new CommandCenterWindow { DataContext = _services.GetRequiredService<CommandCenterViewModel>() };
        window.Deactivated += (_, _) => _commandCenterDeactivatedAt = Environment.TickCount64;
        window.Attach(
            _services.GetRequiredService<IThumbnailService>(),
            _services.GetRequiredService<WindowPlacementStore>(),
            _services.GetRequiredService<ILogger<CommandCenterWindow>>());
        return window;
    }
}
