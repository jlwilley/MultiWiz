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
using MultiWiz.Core.Storage;
using MultiWiz.Core.Switching;

namespace MultiWiz.App;

public partial class App : Application
{
    private readonly AppHost? _host;
    private readonly SingleInstanceGuard? _instance;
    private readonly SmokeTest? _smokeTest;
    private ILogger<App>? _logger;
    private bool _showingErrorDialog;
    private bool _shutDown;

    /// <summary>Used by the XAML runtime loader and design tools; the running app uses the other constructor.</summary>
    public App()
    {
    }

    /// <param name="host">The composition root.</param>
    /// <param name="instance">The single-instance guard; null in a smoke test, which runs beside a normal instance.</param>
    /// <param name="smokeTest">The CI smoke test driving this run, or null (every normal run).</param>
    internal App(AppHost host, SingleInstanceGuard? instance, SmokeTest? smokeTest)
    {
        _host = host;
        _instance = instance;
        _smokeTest = smokeTest;
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
        var failedHotkeys = hotkeys.FailedActions.Count;
        if (failedHotkeys > 0)
        {
            services.GetRequiredService<StatusService>().Show(
                failedHotkeys == 1
                    ? "1 hotkey could not be registered, usually because another program uses it. See Settings → Hotkeys."
                    : $"{failedHotkeys} hotkeys could not be registered, usually because another program uses them. See Settings → Hotkeys.",
                isError: true);
        }

        services.GetRequiredService<ISessionEvents>().LoginCompleted +=
            (_, _) => Dispatcher.UIThread.Post(windows.ShowMainWindow);

        services.GetRequiredService<OverlayManager>().Start();
        if (_smokeTest is null)
        {
            services.GetRequiredService<UpdateService>().Start();
        }

        _instance?.ListenForActivation(() => Dispatcher.UIThread.Post(windows.ShowMainWindow));

        // After the main window is up: damaged data files, then the one-time MultiWiz 3 import offer.
        Dispatcher.UIThread.Post(() => _ = ShowStartupNoticesAsync(services), DispatcherPriority.Background);

        _smokeTest?.Start(desktop, services);
    }

    private async Task ShowStartupNoticesAsync(IServiceProvider services)
    {
        try
        {
            await ReportSetAsideFilesAsync(services);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Could not report the data files that were set aside");
        }

        try
        {
            await services.GetRequiredService<LegacyImportService>().OfferOnStartupAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "The MultiWiz 3 import offer failed");
        }
    }

    /// <summary>
    /// Tells the user when a data file could not be read and was moved aside (see <see cref="JsonAccountStore.RecoveredFromCorruptFile"/>):
    /// MultiWiz then started without those accounts, teams or settings, and the old data is still in that file.
    /// </summary>
    private static async Task ReportSetAsideFilesAsync(IServiceProvider services)
    {
        var setAside = new[]
            {
                services.GetRequiredService<JsonAccountStore>().RecoveredFromCorruptFile,
                services.GetRequiredService<JsonTeamStore>().RecoveredFromCorruptFile,
                services.GetRequiredService<JsonSettingsStore>().RecoveredFromCorruptFile,
            }
            .OfType<string>()
            .ToArray();
        if (setAside.Length == 0)
        {
            return;
        }

        await services.GetRequiredService<IDialogService>().ShowErrorAsync(
            "Some MultiWiz data could not be read",
            "These files were damaged, so MultiWiz set them aside and started without the accounts, teams or settings " +
            "in them. Nothing in them was deleted: to look at them, open About → Open data folder.",
            string.Join(Environment.NewLine, setAside));
    }

    private void ReportUnhandledException(Exception exception)
    {
        _logger?.LogError(exception, "Unhandled exception on the UI thread");
        if (_smokeTest is not null)
        {
            _smokeTest.Fail("Unhandled exception on the UI thread", exception);
            return;
        }

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
            _smokeTest?.Fail($"Shutdown step '{step}' failed", ex);
        }
    }
}
