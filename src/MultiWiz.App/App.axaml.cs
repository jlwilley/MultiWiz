using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiWiz.App.Overlays;
using MultiWiz.App.Services;
using MultiWiz.App.ViewModels;
using MultiWiz.Core.Hotkeys;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Sessions;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Switching;

namespace MultiWiz.App;

public partial class App : Application
{
    private readonly AppHost? _host;
    private readonly SingleInstanceGuard? _instance;
    private ILogger<App>? _logger;
    private bool _showingErrorDialog;
    private bool _shutDown;

    /// <summary>Used by the XAML runtime loader and design tools; the running app uses the other constructor.</summary>
    public App()
    {
    }

    internal App(AppHost host, SingleInstanceGuard instance)
    {
        _host = host;
        _instance = instance;
    }

    public override void Initialize()
    {
        if (_host is not null)
        {
            // The tray icon's bindings resolve against Application.DataContext.
            DataContext = _host.Services.GetRequiredService<TrayViewModel>();
        }

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (_host is not null && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Start(desktop, _host.Services);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void Start(IClassicDesktopStyleApplicationLifetime desktop, IServiceProvider services)
    {
        _logger = services.GetRequiredService<ILogger<App>>();
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            ReportUnhandledException(e.Exception);
        };

        services.GetRequiredService<ThemeService>().Start();

        var windows = services.GetRequiredService<WindowCoordinator>();
        desktop.MainWindow = windows.MainWindow;
        desktop.Exit += (_, _) => CleanUpOnExit(services);

        var hotkeys = services.GetRequiredService<IHotkeyCoordinator>();
        hotkeys.UiActionRequested += (_, action) => Dispatcher.UIThread.Post(() => windows.HandleHotkey(action));
        hotkeys.Start();

        services.GetRequiredService<ISessionEvents>().LoginCompleted +=
            (_, _) => Dispatcher.UIThread.Post(windows.ShowMainWindow);

        services.GetRequiredService<OverlayManager>().Start();
        services.GetRequiredService<UpdateService>().Start();
        _instance?.ListenForActivation(() => Dispatcher.UIThread.Post(windows.ShowMainWindow));

        // After the main window is up: the one-time MultiWiz 3 import offer.
        Dispatcher.UIThread.Post(
            () => _ = OfferLegacyImportAsync(services.GetRequiredService<LegacyImportService>()),
            DispatcherPriority.Background);
    }

    private async Task OfferLegacyImportAsync(LegacyImportService legacyImport)
    {
        try
        {
            await legacyImport.OfferOnStartupAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "The MultiWiz 3 import offer failed");
        }
    }

    private void ReportUnhandledException(Exception exception)
    {
        _logger?.LogError(exception, "Unhandled exception on the UI thread");
        if (_showingErrorDialog || _shutDown || _host is null)
        {
            return;
        }

        _showingErrorDialog = true;
        var dialogs = _host.Services.GetRequiredService<IDialogService>();
        Dispatcher.UIThread.Post(() => _ = ShowErrorDialogAsync(dialogs, exception));
    }

    private async Task ShowErrorDialogAsync(IDialogService dialogs, Exception exception)
    {
        try
        {
            await dialogs.ShowErrorAsync(
                "Something went wrong",
                "MultiWiz ran into an unexpected problem but is still running. The details were saved to the log " +
                "(About → Open log folder).",
                exception.Message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Could not show the error dialog");
        }
        finally
        {
            _showingErrorDialog = false;
        }
    }

    /// <summary>Puts every game back the way MultiWiz found it (volume, priority) and stops background work.</summary>
    private void CleanUpOnExit(IServiceProvider services)
    {
        if (_shutDown)
        {
            return;
        }

        _shutDown = true;
        _logger?.LogInformation("Shutting down");

        RunShutdownStep("pending edits", () =>
        {
            services.GetRequiredService<SettingsPageViewModel>().FlushPendingChanges();
            services.GetRequiredService<TeamsPageViewModel>().FlushPendingChanges();
            services.GetRequiredService<SwitcherViewModel>().FlushPendingChanges();
        });
        RunShutdownStep("overlays", () => services.GetRequiredService<OverlayManager>().Dispose());
        RunShutdownStep("updates", () => services.GetRequiredService<UpdateService>().Dispose());
        RunShutdownStep("hotkeys", () => services.GetRequiredService<IHotkeyCoordinator>().Stop());

        // Stop the switcher before restoring anything: its debounced focus effects would otherwise mute and throttle
        // background clients again when closing MultiWiz hands the foreground to a game. Disposing waits for a
        // running effects pass and prevents any later one.
        RunShutdownStep("switcher", () => services.GetRequiredService<ClientSwitcher>().Dispose());

        var sessions = services.GetRequiredService<ISessionManager>();
        var running = sessions.Sessions;
        if (services.GetRequiredService<ISettingsStore>().Current.General.CloseGamesOnExit)
        {
            RunShutdownStep("stop clients", sessions.StopAll);
        }

        RunShutdownStep("audio", () => services.GetRequiredService<IAudioService>().RestoreAll());
        RunShutdownStep("priorities", () =>
        {
            var throttler = services.GetRequiredService<IProcessThrottler>();
            foreach (var session in running.Where(session => session.ProcessId != 0))
            {
                throttler.Release(session.ProcessId);
            }
        });
    }

    private void RunShutdownStep(string step, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Shutdown step '{Step}' failed", step);
        }
    }
}
