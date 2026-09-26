using Avalonia;
using Avalonia.Controls;
using MultiWiz.Core.Storage;
using Velopack;

namespace MultiWiz.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: install/update/uninstall hooks launch the exe and exit inside Run().
        var firstRunAfterInstall = false;
        VelopackApp.Build()
            .OnFirstRun(_ => firstRunAfterInstall = true)
            .Run();

        using var instance = SingleInstanceGuard.TryAcquire();
        if (instance is null)
        {
            SingleInstanceGuard.SignalRunningInstance();
            return 0;
        }

        using var host = AppHost.Create(AppPaths.Default);
        if (firstRunAfterInstall)
        {
            host.OnFirstRunAfterInstall();
        }

        try
        {
            // The tray icon keeps the app alive; it exits only through an explicit Shutdown().
            return AppBuilder.Configure(() => new App(host, instance))
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        catch (Exception ex)
        {
            host.LogFatal(ex, "MultiWiz crashed");
            return 1;
        }
    }
}
