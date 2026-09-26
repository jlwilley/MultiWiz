using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MultiWiz.App.ViewModels;
using MultiWiz.App.Views;
using MultiWiz.App.Views.Dialogs;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Storage;

namespace MultiWiz.App.Services;

/// <summary>
/// Hidden runtime check for CI: <c>MultiWiz.exe --smoke-test [--smoke-test-result &lt;file&gt;]</c> starts the app
/// against a throwaway data folder (no single-instance guard, no update checks), opens every page, window and dialog
/// once, cancels the dialogs itself and quits. The exit code is 0 when nothing threw, otherwise 1; failures also go to
/// stderr, the log and the result file ("OK" or the error text). A 90-second watchdog ends a hung run with 1.
/// Nothing here runs without the argument.
/// </summary>
internal sealed class SmokeTest
{
    public const string Argument = "--smoke-test";
    private const string ResultArgument = "--smoke-test-result";
    private static readonly TimeSpan WatchdogTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(10);

    private readonly string _root;
    private readonly string? _resultFile;
    private readonly Lock _lock = new();
    private readonly List<string> _failures = [];
    private Timer? _watchdog;
    private ILogger? _logger;
    private bool _walkFinished;
    private bool _completed;

    private SmokeTest(string root, string? resultFile)
    {
        _root = root;
        _resultFile = resultFile;
        Paths = new AppPaths(Path.Combine(root, "Roaming"), Path.Combine(root, "Local"));
    }

    /// <summary>The throwaway data and log folders the run uses instead of %AppData% and %LocalAppData%.</summary>
    public AppPaths Paths { get; }

    private bool HasFailed
    {
        get
        {
            lock (_lock)
            {
                return _failures.Count > 0;
            }
        }
    }

    /// <summary>True when the command line asks for a smoke test.</summary>
    public static bool IsRequested(string[] args) => args.Contains(Argument, StringComparer.OrdinalIgnoreCase);

    /// <summary>Starts the smoke test's watchdog and exception handlers when <paramref name="args"/> asks for one; otherwise null.</summary>
    public static SmokeTest? FromArgs(string[] args)
    {
        if (!IsRequested(args))
        {
            return null;
        }

        var resultIndex = Array.FindIndex(args, arg => string.Equals(arg, ResultArgument, StringComparison.OrdinalIgnoreCase));
        var resultFile = resultIndex >= 0 && resultIndex + 1 < args.Length ? Path.GetFullPath(args[resultIndex + 1]) : null;
        var root = Path.Combine(Path.GetTempPath(), "MultiWiz-smoke-" + Guid.NewGuid().ToString("N"));

        var smokeTest = new SmokeTest(root, resultFile);
        AppDomain.CurrentDomain.UnhandledException += smokeTest.OnUnhandledException;
        TaskScheduler.UnobservedTaskException += smokeTest.OnUnobservedTaskException;
        smokeTest._watchdog = new Timer(_ => smokeTest.OnWatchdog(), null, WatchdogTimeout, Timeout.InfiniteTimeSpan);
        Console.Out.WriteLine($"MultiWiz smoke test: data folder {root}");
        return smokeTest;
    }

    /// <summary>Records a failure (stderr and log now, the result file at the end).</summary>
    public void Fail(string context, Exception? exception = null)
    {
        var text = exception is null ? context : $"{context}: {exception}";
        lock (_lock)
        {
            _failures.Add(text);
        }

        Console.Error.WriteLine($"MultiWiz smoke test FAILED: {text}");
        try
        {
            _logger?.LogCritical(exception, "Smoke test failure: {Context}", context);
        }
        catch (Exception)
        {
            // The log is gone during the last moments of shutdown; stderr and the result file still have it.
        }
    }

    /// <summary>Starts the scripted walk through the UI once the main window is up. UI thread, from App.Start.</summary>
    public void Start(IClassicDesktopStyleApplicationLifetime desktop, IServiceProvider services)
    {
        _logger = services.GetRequiredService<ILogger<SmokeTest>>();
        _logger.LogInformation("Smoke test started with data folder {Root}", _root);
        Dispatcher.UIThread.Post(() => _ = RunAsync(desktop, services), DispatcherPriority.Background);
    }

    /// <summary>Writes the result file, removes the data folder after a pass, and returns the process exit code.</summary>
    public int Complete(int exitCode)
    {
        lock (_lock)
        {
            if (_completed)
            {
                return 1;
            }

            _completed = true;
        }

        _watchdog?.Dispose();
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        if (exitCode != 0 && !HasFailed)
        {
            Fail($"MultiWiz exited with code {exitCode}");
        }

        if (!_walkFinished && !HasFailed)
        {
            Fail("MultiWiz exited before the smoke test had opened every window");
        }

        if (HasFailed)
        {
            WriteResult(FailureText());
            return 1;
        }

        WriteResult("OK");
        Console.Out.WriteLine("MultiWiz smoke test passed");
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Out.WriteLine($"Could not delete {_root}: {ex.Message}");
        }

        return 0;
    }

    private async Task RunAsync(IClassicDesktopStyleApplicationLifetime desktop, IServiceProvider services)
    {
        var windows = services.GetRequiredService<WindowCoordinator>();
        try
        {
            var main = services.GetRequiredService<MainWindowViewModel>();
            var accounts = services.GetRequiredService<AccountsPageViewModel>();
            var teams = services.GetRequiredService<TeamsPageViewModel>();
            await PauseAsync("main window");

            // Every page empty first, then again with an account so the list and slot templates are built too.
            await VisitPagesAsync(main, windows.MainWindow);
            services.GetRequiredService<IAccountStore>().Upsert(new Account
            {
                Id = Guid.NewGuid(),
                DisplayName = "Smoke Test",
                Username = "smoke-test",
                AccentColor = "#CBA6F7",
            });
            await VisitPagesAsync(main, windows.MainWindow);

            // Account editor (a modal dialog): open it the way "Add account" does, then cancel it.
            main.SelectedPageIndex = 0;
            var adding = accounts.AddAccountCommand.ExecuteAsync(null);
            await PauseAsync("account editor");
            GetDataContext<AccountEditorWindow, AccountEditorViewModel>(desktop).CancelCommand.Execute(null);
            await adding.WaitAsync(DialogTimeout);

            // Team editor (inline on the Teams page): a new team with the account in a slot, then deleted through
            // its confirmation dialog.
            main.SelectedPageIndex = 1;
            teams.NewTeamCommand.Execute(null);
            await PauseAsync("new team");
            var editor = teams.Editor ?? throw new InvalidOperationException("The team editor did not open for the new team.");
            editor.AddSlotCommand.Execute(null);
            await PauseAsync("team slot");
            Require(editor.Slots.Count == 1, "The account was not added to the team.");
            var deleting = teams.DeleteTeamCommand.ExecuteAsync(null);
            await PauseAsync("delete team confirmation");
            GetDataContext<MessageDialog, MessageDialogViewModel>(desktop).ConfirmCommand.Execute(null);
            await deleting.WaitAsync(DialogTimeout);
            Require(teams.Teams.Count == 0, "The team was not deleted.");

            windows.ToggleSwitcher();
            await PauseAsync("switcher");
            Require(Find<SwitcherWindow>(desktop).IsVisible, "The switcher did not open.");
            windows.ToggleSwitcher();
            await PauseAsync("switcher hidden");
            Require(!Find<SwitcherWindow>(desktop).IsVisible, "The switcher did not close.");

            windows.ToggleCommandCenter();
            await PauseAsync("Command Center");
            var commandCenter = Find<CommandCenterWindow>(desktop);
            Require(commandCenter.IsVisible, "The Command Center did not open.");

            // What its close button does (the toggle hides it only while it is the active window).
            commandCenter.Hide();
            await PauseAsync("Command Center hidden");

            windows.ToggleNameBadges();
            await PauseAsync("name badges on");
            windows.ToggleNameBadges();
            await PauseAsync("name badges off");

            // Unobserved task exceptions are reported only when their tasks are collected (waited for off the UI thread,
            // so no finalizer can deadlock on it).
            await Task.Run(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            });
            await PauseAsync("finalizers");
            _walkFinished = true;
            _logger?.LogInformation("Smoke test walk finished");
        }
        catch (SmokeTestStoppedException)
        {
            // A failure was already recorded.
        }
        catch (Exception ex)
        {
            Fail("A smoke test step failed", ex);
        }
        finally
        {
            windows.Quit();
        }
    }

    /// <summary>Shows Accounts, Teams, Settings (every tab) and About.</summary>
    private async Task VisitPagesAsync(MainWindowViewModel main, Window mainWindow)
    {
        for (var page = 0; page < 4; page++)
        {
            main.SelectedPageIndex = page;
            await PauseAsync($"page {page}");
            if (page != 2)
            {
                continue;
            }

            foreach (var tabs in mainWindow.GetVisualDescendants().OfType<TabControl>().ToArray())
            {
                for (var tab = 0; tab < tabs.ItemCount; tab++)
                {
                    tabs.SelectedIndex = tab;
                    await PauseAsync($"settings tab {tab}");
                }

                tabs.SelectedIndex = 0;
            }
        }
    }

    /// <summary>Lets the UI lay out and render, then stops the walk if anything failed meanwhile.</summary>
    private async Task PauseAsync(string step)
    {
        await Task.Delay(StepDelay);
        _logger?.LogDebug("Smoke test step done: {Step}", step);
        if (HasFailed)
        {
            throw new SmokeTestStoppedException();
        }
    }

    private static TWindow Find<TWindow>(IClassicDesktopStyleApplicationLifetime desktop)
        where TWindow : Window =>
        desktop.Windows.OfType<TWindow>().LastOrDefault()
            ?? throw new InvalidOperationException($"No {typeof(TWindow).Name} is open.");

    private static TViewModel GetDataContext<TWindow, TViewModel>(IClassicDesktopStyleApplicationLifetime desktop)
        where TWindow : Window
        where TViewModel : class =>
        Find<TWindow>(desktop).DataContext as TViewModel
            ?? throw new InvalidOperationException($"The {typeof(TWindow).Name} has no {typeof(TViewModel).Name}.");

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        Fail($"Unhandled exception (terminating: {e.IsTerminating})", e.ExceptionObject as Exception);
        if (e.IsTerminating)
        {
            // Exit with the smoke test's code instead of the crash code (and without Windows Error Reporting).
            Environment.Exit(Complete(1));
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
        Fail("Unobserved task exception", e.Exception);

    private void OnWatchdog()
    {
        Fail($"The smoke test did not finish within {WatchdogTimeout.TotalSeconds:0} seconds (hang or deadlock)");
        Environment.Exit(Complete(1));
    }

    private string FailureText()
    {
        lock (_lock)
        {
            return string.Join(Environment.NewLine + Environment.NewLine, _failures)
                + $"{Environment.NewLine}{Environment.NewLine}Log folder: {Paths.LogsDirectory}";
        }
    }

    private void WriteResult(string text)
    {
        if (_resultFile is null)
        {
            return;
        }

        try
        {
            if (Path.GetDirectoryName(_resultFile) is { Length: > 0 } folder)
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(_resultFile, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not write the smoke test result to {_resultFile}: {ex.Message}");
        }
    }

    private sealed class SmokeTestStoppedException : Exception
    {
    }
}
