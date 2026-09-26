using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiWiz.App.Overlays;
using MultiWiz.App.Services;
using MultiWiz.App.ViewModels;
using MultiWiz.Core;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Storage;
using MultiWiz.Platform.Windows;
using Serilog;
using Serilog.Events;

namespace MultiWiz.App;

/// <summary>Composition root: logging, Core, the Windows platform layer, and the app's own services and view models.</summary>
public sealed class AppHost : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ILogger<AppHost> _logger;
    private bool _disposed;

    private AppHost(ServiceProvider provider)
    {
        _provider = provider;
        _logger = provider.GetRequiredService<ILogger<AppHost>>();
    }

    public IServiceProvider Services => _provider;

    public static AppHost Create(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        paths.EnsureCreated();

        var serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: Path.Combine(paths.LogsDirectory, "multiwiz-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        Log.Logger = serilogLogger;

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddSerilog(serilogLogger, dispose: true);
        });

        services.AddMultiWizCore(paths);
        services.AddWindowsPlatform();

        // App services (UI-thread affine unless noted otherwise in each class).
        services.AddSingleton<StatusService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<WindowPlacementStore>();
        services.AddSingleton<WindowCoordinator>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<UpdateService>();
        services.AddSingleton<ClientActions>();
        services.AddSingleton<LegacyImportService>();
        services.AddSingleton<GameFilesService>();
        services.AddSingleton<OverlayManager>();

        // View models.
        services.AddSingleton<TrayViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<AccountsPageViewModel>();
        services.AddSingleton<TeamsPageViewModel>();
        services.AddSingleton<SettingsPageViewModel>();
        services.AddSingleton<GameFilesViewModel>();
        services.AddSingleton<AboutPageViewModel>();
        services.AddSingleton<SwitcherViewModel>();
        services.AddSingleton<CommandCenterViewModel>();

        var host = new AppHost(services.BuildServiceProvider());
        host.HookUnhandledExceptions();
        host._logger.LogInformation(
            "MultiWiz {Version} starting on {OS}", AppInfo.Version, Environment.OSVersion.VersionString);
        return host;
    }

    public void LogFatal(Exception exception, string message) => _logger.LogCritical(exception, "{Message}", message);

    /// <summary>
    /// Called on the first run after an install. A prerelease build was installed from the beta setup, so it follows
    /// the Beta update channel; left on the default Stable channel it would never be offered the next beta.
    /// </summary>
    public void OnFirstRunAfterInstall()
    {
        if (!AppInfo.Version.Contains('-'))
        {
            return;
        }

        try
        {
            _provider.GetRequiredService<ISettingsStore>().Update(settings => settings.General.UpdateChannel == UpdateChannel.Beta
                ? settings
                : settings with { General = settings.General with { UpdateChannel = UpdateChannel.Beta } });
            _logger.LogInformation("First run of beta {Version}: following the Beta update channel", AppInfo.Version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not switch the update channel to Beta");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _logger.LogInformation("MultiWiz stopped");

        // Disposes every singleton (platform threads, hooks, audio worker) and flushes the Serilog file sink.
        _provider.Dispose();
    }

    private void HookUnhandledExceptions()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _logger.LogCritical(exception, "Unhandled exception (terminating: {Terminating})", e.IsTerminating);
        }

        if (e.IsTerminating)
        {
            Log.CloseAndFlush();
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger.LogError(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}
