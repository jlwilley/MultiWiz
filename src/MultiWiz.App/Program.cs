using Avalonia;
using Avalonia.Controls;
using MultiWiz.App.Services;
using MultiWiz.Core.Storage;
using Velopack;

namespace MultiWiz.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: install/update/uninstall hooks launch the exe and exit inside Run(). Applying a downloaded
        // update here force-stops every MultiWiz process, so it may only happen when no other instance runs: a second
        // launch (the usual way back to a window hidden in the tray) must not kill the running one before it can put
        // its games back. The running instance applies the update when the user chooses "Restart now".
        var firstRunAfterInstall = false;
        VelopackApp.Build()
            .SetAutoApplyOnStartup(!SmokeTest.IsRequested(args) && !SingleInstanceGuard.IsAnotherInstanceRunning())
            .OnFirstRun(_ => firstRunAfterInstall = true)
            .Run();

        // Hidden CI mode (MultiWiz.exe --smoke-test); null in every normal run.
        var smokeTest = SmokeTest.FromArgs(args);

        using var instance = smokeTest is null ? SingleInstanceGuard.TryAcquire() : null;
        if (smokeTest is null && instance is null)
        {
            SingleInstanceGuard.SignalRunningInstance();
            return 0;
        }

        AppHost host;
        try
        {
            host = AppHost.Create(smokeTest?.Paths ?? AppPaths.Default);
        }
        catch (Exception ex)
        {
            // No log exists yet (creating the data or log folder is what usually fails here). Let a new launch start
            // while the message is on screen.
            instance?.Dispose();
            ReportStartupFailure(ex, smokeTest);
            return smokeTest?.Complete(1) ?? 1;
        }

        int exitCode;
        using (host)
        {
            if (firstRunAfterInstall)
            {
                host.OnFirstRunAfterInstall();
            }

            try
            {
                // The tray icon keeps the app alive; it exits only through an explicit Shutdown().
                exitCode = AppBuilder.Configure(() => new App(host, instance, smokeTest))
                    .UsePlatformDetect()
                    .WithInterFont()
                    .LogToTrace()
                    .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
            }
            catch (Exception ex)
            {
                host.LogFatal(ex, "MultiWiz crashed");
                exitCode = 1;
                if (smokeTest is not null)
                {
                    smokeTest.Fail("MultiWiz crashed", ex);
                }
                else
                {
                    // Put the games' volumes back, stop the platform threads and flush the log before waiting on the user,
                    // and let a new launch start while the message is on screen.
                    DisposeQuietly(host);
                    instance?.Dispose();
                    NativeDialog.ShowError(
                        $"MultiWiz stopped because of an unexpected error:{Environment.NewLine}{Environment.NewLine}{ex.Message}" +
                        $"{Environment.NewLine}{Environment.NewLine}The details are in the log in {AppPaths.Default.LogsDirectory}.");
                }
            }
        }

        return smokeTest?.Complete(exitCode) ?? exitCode;
    }

    private static void ReportStartupFailure(Exception exception, SmokeTest? smokeTest)
    {
        if (smokeTest is not null)
        {
            smokeTest.Fail("MultiWiz could not start", exception);
            return;
        }

        string? detailsFile = Path.Combine(Path.GetTempPath(), "MultiWiz-startup-error.txt");
        try
        {
            File.WriteAllText(detailsFile, exception.ToString());
        }
        catch (Exception)
        {
            detailsFile = null;
        }

        NativeDialog.ShowError(
            $"MultiWiz could not start:{Environment.NewLine}{Environment.NewLine}{exception.Message}" +
            (detailsFile is null ? string.Empty : $"{Environment.NewLine}{Environment.NewLine}Details were saved to {detailsFile}."));
    }

    private static void DisposeQuietly(AppHost host)
    {
        try
        {
            host.Dispose();
        }
        catch (Exception)
        {
            // Best effort after a crash; the dialog matters more.
        }
    }
}
