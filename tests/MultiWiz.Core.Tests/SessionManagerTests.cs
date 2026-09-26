using MultiWiz.Core.Platform;
using MultiWiz.Core.Sessions;
using MultiWiz.Core.Storage;
using MultiWiz.Core.Tests.Fakes;
using MultiWiz.Core.Tests.Support;

namespace MultiWiz.Core.Tests;

public sealed class SessionManagerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Launch_starts_the_client_types_credentials_and_reaches_running()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin(refocus: true));
        var account = h.AddAccount("Storm Main", password: "p@ss word");
        var states = new List<ClientSessionState>();
        h.Manager.SessionChanged += (_, session) =>
        {
            lock (states)
            {
                states.Add(session.State);
            }
        };
        ClientSession? loginCompleted = null;
        h.Manager.LoginCompleted += (_, session) => loginCompleted = session;

        var session = await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(account.Id, Ct));

        Assert.Equal(ClientSessionState.Running, session.State);
        Assert.Null(session.Error);
        var launch = Assert.Single(h.Launcher.Launches);
        Assert.Equal(h.StandaloneInstall.ExecutablePath, launch.Request.ExecutablePath);
        Assert.Equal(h.StandaloneInstall.BinPath, launch.Request.WorkingDirectory);
        Assert.Equal("-L login.us.wizard101.com 12000", launch.Request.Arguments);

        var window = FakeWindowService.WindowFor(launch.Process.Id);
        Assert.Equal(launch.Process.Id, session.ProcessId);
        Assert.Equal(window, session.WindowHandle);
        Assert.Equal(
            new[] { $"{window}:text:{account.Username}", $"{window}:key:9", $"{window}:text:p@ss word", $"{window}:key:13" },
            h.Input.Log);
        Assert.Equal(
            new[]
            {
                ClientSessionState.Launching, ClientSessionState.WaitingForWindow, ClientSessionState.WaitingForReady,
                ClientSessionState.LoggingIn, ClientSessionState.Running,
            },
            states);
        Assert.NotNull(loginCompleted);
        Assert.Equal(account.Id, loginCompleted.AccountId);
        Assert.Equal(session, h.Manager.Find(account.Id));
        Assert.Equal(session, h.Manager.FindByProcessId(session.ProcessId));
        Assert.Equal(session, h.Manager.FindByWindow(window));
        Assert.Equal(session, Assert.Single(h.Manager.Sessions));
    }

    [Fact]
    public async Task Launch_waits_the_ready_delay_before_typing()
    {
        var login = SessionHarness.FastLogin() with { ReadyDelaySeconds = 5 };
        using var h = new SessionHarness(login);
        var account = h.AddAccount("Patient");
        var started = h.Time.GetUtcNow();
        DateTimeOffset? typingStarted = null;
        h.Manager.SessionChanged += (_, session) =>
        {
            if (session.State == ClientSessionState.LoggingIn)
            {
                typingStarted = h.Time.GetUtcNow();
            }
        };

        await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(account.Id, Ct));

        Assert.NotNull(typingStarted);
        Assert.True(typingStarted.Value - started >= TimeSpan.FromSeconds(5), $"Typing started after {typingStarted.Value - started}");
    }

    [Fact]
    public async Task Launch_without_auto_login_goes_straight_to_running()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin(autoLogin: false, refocus: true));
        var account = h.AddAccount("Manual", password: null);
        var loginCompleted = false;
        h.Manager.LoginCompleted += (_, _) => loginCompleted = true;

        var session = await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(account.Id, Ct));

        Assert.Equal(ClientSessionState.Running, session.State);
        Assert.Empty(h.Input.Log);
        Assert.False(loginCompleted);
    }

    [Fact]
    public async Task Launch_fails_with_a_clear_message_when_the_game_is_not_installed()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        h.Locator.Installs.Clear();
        var account = h.AddAccount("Nowhere");

        var session = await h.Manager.LaunchAsync(account.Id, Ct);

        Assert.Equal(ClientSessionState.Failed, session.State);
        Assert.Equal("Wizard101 was not found. Set the game folder in Settings → Games.", session.Error);
        Assert.Empty(h.Launcher.Launches);
        Assert.Empty(h.Manager.Sessions);
        Assert.Null(h.Manager.Find(account.Id));
    }

    [Fact]
    public async Task Launch_fails_when_the_client_executable_is_missing()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        File.Delete(h.StandaloneInstall.ExecutablePath);
        var account = h.AddAccount("Deleted");

        var session = await h.Manager.LaunchAsync(account.Id, Ct);

        Assert.Equal(ClientSessionState.Failed, session.State);
        Assert.Equal("Wizard101 was not found. Set the game folder in Settings → Games.", session.Error);
        Assert.Empty(h.Launcher.Launches);
    }

    [Fact]
    public async Task Launch_fails_for_an_unknown_account()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());

        var session = await h.Manager.LaunchAsync(Guid.NewGuid(), Ct);

        Assert.Equal(ClientSessionState.Failed, session.State);
        Assert.Empty(h.Launcher.Launches);
    }

    [Fact]
    public async Task Launch_reports_process_start_errors_as_a_failed_session()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        h.Launcher.StartException = new InvalidOperationException("Access is denied.");
        var account = h.AddAccount("Blocked");

        var session = await h.Manager.LaunchAsync(account.Id, Ct);

        Assert.Equal(ClientSessionState.Failed, session.State);
        Assert.Contains("Access is denied.", session.Error);
        Assert.Empty(h.Manager.Sessions);
    }

    [Fact]
    public async Task Launch_fails_and_closes_the_client_when_the_window_never_appears()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin(windowTimeoutSeconds: 3));
        h.Windows.WindowsNeverAppear = true;
        var account = h.AddAccount("Invisible");
        var started = h.Time.GetUtcNow();

        var session = await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(account.Id, Ct));

        Assert.Equal(ClientSessionState.Failed, session.State);
        Assert.Equal("The game window never appeared.", session.Error);
        Assert.True(h.Time.GetUtcNow() - started >= TimeSpan.FromSeconds(3));
        Assert.Equal(1, Assert.Single(h.Launcher.Launches).Process.KillCount);
        Assert.Empty(h.Input.Log);
        Assert.Empty(h.Manager.Sessions);
    }

    [Fact]
    public async Task Launch_fails_before_starting_the_client_when_no_password_is_saved()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var account = h.AddAccount("Forgetful", password: null);

        var session = await h.Manager.LaunchAsync(account.Id, Ct);

        Assert.Equal(ClientSessionState.Failed, session.State);
        Assert.StartsWith("No saved password", session.Error);
        Assert.Empty(h.Launcher.Launches);
        Assert.Empty(h.Input.Log);
        Assert.Empty(h.Manager.Sessions);
    }

    [Fact]
    public async Task A_password_deleted_during_the_launch_fails_it_closes_the_client_and_releases_the_login_gate()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var forgetful = h.AddAccount("Forgetful");
        var prepared = h.AddAccount("Prepared");
        h.Manager.SessionChanged += (_, session) =>
        {
            if (session.AccountId == forgetful.Id && session.State == ClientSessionState.WaitingForReady)
            {
                h.Vault.Delete(forgetful.Id);
            }
        };

        var failed = await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(forgetful.Id, Ct));

        Assert.Equal(ClientSessionState.Failed, failed.State);
        Assert.StartsWith("No saved password", failed.Error);
        Assert.Empty(h.Input.Log);
        Assert.Equal(1, Assert.Single(h.Launcher.Launches).Process.KillCount);

        // The gate was released, so the next client can still log in.
        var next = await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(prepared.Id, Ct));
        Assert.Equal(ClientSessionState.Running, next.State);
        Assert.Equal(4, h.Input.Log.Count);
    }

    [Fact]
    public async Task A_typing_failure_fails_the_launch_and_closes_the_client()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var account = h.AddAccount("Unlucky");
        h.Input.SendException = new InvalidOperationException("The window went away.");

        var session = await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(account.Id, Ct));

        Assert.Equal(ClientSessionState.Failed, session.State);
        Assert.Contains("The window went away.", session.Error);
        Assert.Equal(1, Assert.Single(h.Launcher.Launches).Process.KillCount);
        Assert.Empty(h.Manager.Sessions);
    }

    [Fact]
    public async Task Launch_fails_fast_when_the_process_exits_before_its_window_appears()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin(windowTimeoutSeconds: 60));
        h.Windows.WindowsNeverAppear = true;
        var account = h.AddAccount("Crashy");
        var launchTask = h.Manager.LaunchAsync(account.Id, Ct);
        var process = Assert.Single(h.Launcher.Launches).Process;
        var started = h.Time.GetUtcNow();

        process.Exit();
        var session = await h.Time.RunUntilCompleteAsync(launchTask);

        Assert.Equal(ClientSessionState.Failed, session.State);
        Assert.Equal("The game closed before it finished starting.", session.Error);
        Assert.True(h.Time.GetUtcNow() - started < TimeSpan.FromSeconds(60));
        Assert.Empty(h.Manager.Sessions);
        Assert.Contains(process.Id, h.Audio.Released);
        Assert.Contains(process.Id, h.Throttler.Released);
        Assert.True(process.IsDisposed);
    }

    [Fact]
    public async Task Process_exit_after_login_marks_the_session_exited_and_releases_audio_and_throttling()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var account = h.AddAccount("Leaver");
        var running = await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(account.Id, Ct));
        var process = Assert.Single(h.Launcher.Launches).Process;
        var exitedTask = h.WaitForStateAsync(account.Id, ClientSessionState.Exited);

        process.Exit();
        var exited = await exitedTask;

        Assert.Equal(running.ProcessId, exited.ProcessId);
        Assert.Empty(h.Manager.Sessions);
        Assert.Null(h.Manager.Find(account.Id));
        Assert.Null(h.Manager.FindByProcessId(running.ProcessId));
        Assert.Contains(running.ProcessId, h.Audio.Released);
        Assert.Contains(running.ProcessId, h.Throttler.Released);
    }

    [Fact]
    public async Task Stop_kills_the_client_and_the_session_exits()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var account = h.AddAccount("Stoppable");
        await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(account.Id, Ct));
        var process = Assert.Single(h.Launcher.Launches).Process;
        var exitedTask = h.WaitForStateAsync(account.Id, ClientSessionState.Exited);
        var killsWhenVolumeFirstRestored = -1;
        h.Audio.Releasing = processId =>
        {
            if (processId == process.Id)
            {
                Interlocked.CompareExchange(ref killsWhenVolumeFirstRestored, process.KillCount, -1);
            }
        };

        Assert.True(h.Manager.Stop(account.Id));
        await exitedTask;

        // The volume goes back while the client (and its audio session) still exists.
        Assert.Equal(0, Volatile.Read(ref killsWhenVolumeFirstRestored));
        Assert.Equal(1, process.KillCount);
        Assert.Empty(h.Manager.Sessions);
        Assert.False(h.Manager.Stop(account.Id));
    }

    [Fact]
    public async Task Stop_during_launch_ends_the_launch_as_exited()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin(windowTimeoutSeconds: 60));
        h.Windows.WindowsNeverAppear = true;
        var account = h.AddAccount("Impatient");
        var launchTask = h.Manager.LaunchAsync(account.Id, Ct);
        var process = Assert.Single(h.Launcher.Launches).Process;

        Assert.True(h.Manager.Stop(account.Id));
        var session = await h.Time.RunUntilCompleteAsync(launchTask);

        Assert.Equal(ClientSessionState.Exited, session.State);
        Assert.Equal(1, process.KillCount);
        Assert.Empty(h.Manager.Sessions);
    }

    [Fact]
    public async Task Cancelling_a_launch_closes_the_client_and_throws()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin(windowTimeoutSeconds: 60));
        h.Windows.WindowsNeverAppear = true;
        var account = h.AddAccount("Cancelled");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var launchTask = h.Manager.LaunchAsync(account.Id, cancellation.Token);
        var process = Assert.Single(h.Launcher.Launches).Process;

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => launchTask);
        Assert.Equal(1, process.KillCount);
        Assert.Empty(h.Manager.Sessions);
    }

    [Fact]
    public async Task Launching_an_account_that_is_already_alive_returns_its_session()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var account = h.AddAccount("Twice");
        var first = await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(account.Id, Ct));

        var secondTask = h.Manager.LaunchAsync(account.Id, Ct);

        Assert.True(secondTask.IsCompleted);
        Assert.Equal(first, await secondTask);
        Assert.Single(h.Launcher.Launches);
    }

    [Fact]
    public async Task Steam_installs_are_prepared_and_started_with_the_steam_flag()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var steamInstall = h.AddSteamInstall();
        var account = h.AddAccount("Steamy", installId: steamInstall.Id);

        var session = await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(account.Id, Ct));

        Assert.Equal(ClientSessionState.Running, session.State);
        Assert.Equal(steamInstall.Id, Assert.Single(h.Steam.Calls).Id);
        var launch = Assert.Single(h.Launcher.Launches);
        Assert.Equal(steamInstall.ExecutablePath, launch.Request.ExecutablePath);
        Assert.Equal("-ST -L login.us.wizard101.com 12000", launch.Request.Arguments);
    }

    [Fact]
    public async Task Steam_that_is_not_ready_fails_the_launch_with_its_message()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var steamInstall = h.AddSteamInstall();
        h.Steam.Result = new SteamReadiness(false, "Steam is running but nobody is signed in.");
        var account = h.AddAccount("Offline", installId: steamInstall.Id);

        var session = await h.Manager.LaunchAsync(account.Id, Ct);

        Assert.Equal(ClientSessionState.Failed, session.State);
        Assert.Equal("Steam is running but nobody is signed in.", session.Error);
        Assert.Empty(h.Launcher.Launches);
    }

    [Fact]
    public async Task LaunchMany_staggers_process_starts()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin(staggerSeconds: 2));
        var first = h.AddAccount("First");
        var second = h.AddAccount("Second");

        var sessions = await h.Time.RunUntilCompleteAsync(h.Manager.LaunchManyAsync([first.Id, second.Id], Ct));

        Assert.Equal(new[] { first.Id, second.Id }, sessions.Select(session => session.AccountId).ToArray());
        Assert.All(sessions, session => Assert.Equal(ClientSessionState.Running, session.State));
        var launches = h.Launcher.Launches;
        Assert.Equal(2, launches.Count);
        Assert.True(launches[1].StartedAt - launches[0].StartedAt >= TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task LaunchMany_types_into_one_client_at_a_time()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin(staggerSeconds: 0));
        var accounts = new[] { h.AddAccount("One"), h.AddAccount("Two"), h.AddAccount("Three") };

        var sessions = await h.Time.RunUntilCompleteAsync(h.Manager.LaunchManyAsync(accounts.Select(a => a.Id).ToArray(), Ct));

        Assert.All(sessions, session => Assert.Equal(ClientSessionState.Running, session.State));
        var log = h.Input.Log;
        Assert.Equal(12, log.Count);

        // Without the login gate the three clients (all ready at the same moment) would interleave their keystrokes.
        var windows = new HashSet<string>();
        for (var i = 0; i < log.Count; i += 4)
        {
            var window = log[i][..log[i].IndexOf(':')];
            Assert.True(windows.Add(window), $"Window {window} was typed into twice");
            Assert.All(log.Skip(i).Take(4), entry => Assert.StartsWith(window + ":", entry));
        }
    }

    [Fact]
    public async Task LaunchMany_does_not_wait_after_accounts_that_are_already_running()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin(staggerSeconds: 30));
        var running = h.AddAccount("Running");
        var fresh = h.AddAccount("Fresh");
        await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(running.Id, Ct));
        h.Time.Advance(TimeSpan.FromSeconds(30));
        var before = h.Time.GetUtcNow();

        await h.Time.RunUntilCompleteAsync(h.Manager.LaunchManyAsync([running.Id, fresh.Id], Ct));

        Assert.Equal(2, h.Launcher.Launches.Count);
        Assert.Equal(before, h.Launcher.Launches[1].StartedAt);
    }

    [Fact]
    public async Task Separate_launches_are_staggered_too()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin(staggerSeconds: 3));
        var first = h.AddAccount("First");
        var second = h.AddAccount("Second");

        var firstLaunch = h.Manager.LaunchAsync(first.Id, Ct);
        var secondLaunch = h.Manager.LaunchAsync(second.Id, Ct);
        await h.Time.RunUntilCompleteAsync(Task.WhenAll(firstLaunch, secondLaunch));

        var launches = h.Launcher.Launches;
        Assert.Equal(2, launches.Count);
        Assert.True(launches[1].StartedAt - launches[0].StartedAt >= TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Steam_is_prepared_for_one_launch_at_a_time_and_the_clients_still_start_staggered()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin(staggerSeconds: 2));
        var steamInstall = h.AddSteamInstall();
        var first = h.AddAccount("Steam One", installId: steamInstall.Id);
        var second = h.AddAccount("Steam Two", installId: steamInstall.Id);
        var signedIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Steam.SignedIn = signedIn.Task;

        // Both launches reach Steam while it is still signing in.
        var launches = h.Manager.LaunchManyAsync([first.Id, second.Id], Ct);
        signedIn.SetResult();
        var sessions = await h.Time.RunUntilCompleteAsync(launches);

        Assert.All(sessions, session => Assert.Equal(ClientSessionState.Running, session.State));
        Assert.Equal(2, h.Steam.Calls.Count);
        Assert.Equal(1, h.Steam.MaxConcurrentCalls);
        var starts = h.Launcher.Launches;
        Assert.Equal(2, starts.Count);
        Assert.True(starts[1].StartedAt - starts[0].StartedAt >= TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task StopAll_stops_every_running_client()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin(staggerSeconds: 0));
        var first = h.AddAccount("A");
        var second = h.AddAccount("B");
        await h.Time.RunUntilCompleteAsync(h.Manager.LaunchManyAsync([first.Id, second.Id], Ct));
        var firstExited = h.WaitForStateAsync(first.Id, ClientSessionState.Exited);
        var secondExited = h.WaitForStateAsync(second.Id, ClientSessionState.Exited);

        h.Manager.StopAll();
        await Task.WhenAll(firstExited, secondExited);

        Assert.All(h.Launcher.Launches, launch => Assert.Equal(1, launch.Process.KillCount));
        Assert.Empty(h.Manager.Sessions);
    }

    [Fact]
    public async Task Unknown_realm_falls_back_to_the_default_realm_of_the_game()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var account = h.Accounts.Add("Lost", realmId: "deleted-realm");
        h.Vault.Save(account.Id, account.Username, "pw");

        await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(account.Id, Ct));

        Assert.Equal("-L login.us.wizard101.com 12000", Assert.Single(h.Launcher.Launches).Request.Arguments);
    }

    [Fact]
    public async Task Started_clients_are_recorded_until_they_exit()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var account = h.AddAccount("Storm");
        var session = await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(account.Id, Ct));
        var process = Assert.Single(h.Launcher.Launches).Process;

        var record = Assert.Single(ReadRunningClients(h));
        Assert.Equal(account.Id, record.AccountId);
        Assert.Equal(session.ProcessId, record.ProcessId);
        Assert.Equal(process.StartTime!.Value, record.StartedAt);

        var exited = h.WaitForStateAsync(account.Id, ClientSessionState.Exited);
        process.Exit();
        await exited;

        Assert.Empty(ReadRunningClients(h));
    }

    [Fact]
    public async Task Clients_still_running_after_a_restart_are_picked_up_again()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var account = h.AddAccount("Storm");
        var launched = await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(account.Id, Ct));

        // MultiWiz quits (or restarts for an update) without closing the game, and starts again.
        var restarted = h.CreateManager();

        var adopted = restarted.Find(account.Id);
        Assert.NotNull(adopted);
        Assert.Equal(ClientSessionState.Running, adopted.State);
        Assert.Equal(launched.ProcessId, adopted.ProcessId);
        Assert.Equal(launched.WindowHandle, adopted.WindowHandle);

        // Launching the account again does not start a second client that would log the first one out.
        Assert.Equal(adopted, await restarted.LaunchAsync(account.Id, Ct));
        Assert.Single(h.Launcher.Launches);

        // The adopted client is managed like a launched one: stopping it kills it and ends the session.
        var exited = h.WaitForStateAsync(account.Id, ClientSessionState.Exited, restarted);
        Assert.True(restarted.Stop(account.Id));
        await exited;
        Assert.Equal(1, h.Launcher.Launches[0].Process.KillCount);
        Assert.Empty(restarted.Sessions);
        Assert.Empty(h.CreateManager().Sessions);
    }

    [Fact]
    public void Recorded_clients_that_are_gone_or_whose_id_was_reused_are_not_picked_up()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var reusedId = h.AddAccount("Reused id");
        var otherProgram = h.AddAccount("Other program");
        var exited = h.AddAccount("Exited");
        var deleted = Guid.NewGuid();
        var startedAt = h.Time.GetUtcNow();
        h.Launcher.AddRunning(new FakeLaunchedProcess(7001) { StartTime = startedAt + TimeSpan.FromHours(2) });
        h.Launcher.AddRunning(new FakeLaunchedProcess(7002) { StartTime = startedAt, ProcessName = "notepad" });
        h.Launcher.AddRunning(new FakeLaunchedProcess(7004) { StartTime = startedAt });
        JsonFileStore.Save(
            h.Paths.RunningClientsFile,
            new RunningClientsDocument
            {
                Clients =
                [
                    new RunningClient { AccountId = reusedId.Id, ProcessId = 7001, StartedAt = startedAt },
                    new RunningClient { AccountId = otherProgram.Id, ProcessId = 7002, StartedAt = startedAt },
                    new RunningClient { AccountId = exited.Id, ProcessId = 7003, StartedAt = startedAt },
                    new RunningClient { AccountId = deleted, ProcessId = 7004, StartedAt = startedAt },
                ],
            },
            CoreJsonContext.Default.RunningClientsDocument);

        var manager = h.CreateManager();

        Assert.Empty(manager.Sessions);
        Assert.Empty(ReadRunningClients(h));
    }

    private static IReadOnlyList<RunningClient> ReadRunningClients(SessionHarness h) =>
        JsonFileStore.Load(h.Paths.RunningClientsFile, CoreJsonContext.Default.RunningClientsDocument)?.Clients ?? [];
}
