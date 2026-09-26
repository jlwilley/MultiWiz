using MultiWiz.Core.Games;
using MultiWiz.Core.Sessions;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Storage;
using MultiWiz.Core.Tests.Fakes;
using MultiWiz.Core.Tests.Support;

namespace MultiWiz.Core.Tests;

/// <summary>Clients started outside MultiWiz (the official launcher, or before MultiWiz ran) are picked up and can be linked.</summary>
public sealed class ExternalClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void An_untracked_game_window_is_adopted_as_an_external_session()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var process = StartOutside(h, 7100, age: TimeSpan.FromMinutes(5));
        var window = h.Windows.AddGameWindow(7100);
        var published = new List<ClientSession>();
        h.Manager.SessionChanged += (_, session) => published.Add(session);

        h.Manager.ScanForExternalClients();

        var session = Assert.Single(h.Manager.Sessions);
        Assert.True(session.IsExternal);
        Assert.Equal("Wizard101 client 1", session.Label);
        Assert.Equal(GameKind.Wizard101, session.Game);
        Assert.Equal(ClientSessionState.Running, session.State);
        Assert.Equal(7100, session.ProcessId);
        Assert.Equal(window, session.WindowHandle);
        Assert.Equal(process.StartTime, session.StartedAt);
        Assert.Equal(SessionManager.ExternalSessionId(7100, process.StartTime!.Value), session.AccountId);
        Assert.Equal(session, Assert.Single(published));
        Assert.Equal(session, h.Manager.FindByProcessId(7100));
        Assert.Equal(session, h.Manager.FindByWindow(window));
        Assert.Null(h.Accounts.Find(session.AccountId));

        // Scanning again changes nothing, and external clients are never recorded for re-adoption after a restart.
        h.Manager.ScanForExternalClients();
        Assert.Single(published);
        Assert.Empty(ReadRunningClients(h));
    }

    [Fact]
    public void The_periodic_scan_picks_up_clients_without_being_asked()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        StartOutside(h, 7100, age: TimeSpan.FromMinutes(1));
        h.Windows.AddGameWindow(7100);

        h.Time.Advance(TimeSpan.FromSeconds(4));

        Assert.True(Assert.Single(h.Manager.Sessions).IsExternal);
    }

    [Fact]
    public void External_clients_are_numbered_per_game_and_labels_are_reused_after_an_exit()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var first = StartOutside(h, 7101, age: TimeSpan.FromMinutes(3));
        StartOutside(h, 7102, age: TimeSpan.FromMinutes(2));
        StartOutside(h, 7103, age: TimeSpan.FromMinutes(1), game: GameKind.Pirate101);
        h.Windows.AddGameWindow(7101);
        h.Windows.AddGameWindow(7102);
        h.Windows.AddGameWindow(7103);

        h.Manager.ScanForExternalClients();

        Assert.Equal("Wizard101 client 1", h.Manager.FindByProcessId(7101)?.Label);
        Assert.Equal("Wizard101 client 2", h.Manager.FindByProcessId(7102)?.Label);
        Assert.Equal("Pirate101 client 1", h.Manager.FindByProcessId(7103)?.Label);
    }

    [Fact]
    public async Task Tracked_processes_and_other_programs_are_ignored_and_young_clients_wait()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var account = h.AddAccount("Storm");
        var launched = await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(account.Id, Ct));
        h.Windows.AddGameWindow(launched.ProcessId);
        h.Time.Advance(TimeSpan.FromMinutes(1));

        h.Launcher.AddRunning(new FakeLaunchedProcess(7200) { StartTime = h.Time.GetUtcNow(), ProcessName = "notepad" });
        h.Windows.AddGameWindow(7200);
        StartOutside(h, 7201, age: TimeSpan.FromSeconds(2));
        h.Windows.AddGameWindow(7201);

        h.Manager.ScanForExternalClients();

        Assert.Equal(account.Id, Assert.Single(h.Manager.Sessions).AccountId);

        // Once it is old enough not to be one MultiWiz is starting, the young client is picked up.
        h.Time.Advance(TimeSpan.FromSeconds(10));
        h.Manager.ScanForExternalClients();

        Assert.Equal(2, h.Manager.Sessions.Count);
        Assert.True(h.Manager.FindByProcessId(7201)?.IsExternal);
        Assert.Null(h.Manager.FindByProcessId(7200));
        Assert.False(h.Manager.FindByProcessId(launched.ProcessId)!.IsExternal);
    }

    [Fact]
    public async Task An_external_client_that_exits_is_removed_and_its_audio_and_throttling_released()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var process = StartOutside(h, 7300, age: TimeSpan.FromMinutes(1));
        h.Windows.AddGameWindow(7300);
        h.Manager.ScanForExternalClients();
        var session = Assert.Single(h.Manager.Sessions);
        var exited = h.WaitForStateAsync(session.AccountId, ClientSessionState.Exited);

        process.Exit();
        await exited;

        Assert.Empty(h.Manager.Sessions);
        Assert.Contains(7300, h.Audio.Released);
        Assert.Contains(7300, h.Throttler.Released);
        Assert.True(process.IsDisposed);
    }

    [Fact]
    public async Task External_clients_cannot_retype_a_login_until_linked()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        StartOutside(h, 7400, age: TimeSpan.FromMinutes(1));
        h.Windows.AddGameWindow(7400);
        h.Manager.ScanForExternalClients();
        var external = Assert.Single(h.Manager.Sessions);

        var error = await h.Manager.RetypeLoginAsync(external.AccountId, Ct);

        Assert.Equal("This client was started outside MultiWiz. Link it to an account first.", error);
        Assert.Empty(h.Input.Log);

        // Launching its synthetic id does not start anything either.
        Assert.Equal(external, await h.Manager.LaunchAsync(external.AccountId, Ct));
        Assert.Empty(h.Launcher.Launches);
    }

    [Fact]
    public async Task Linking_re_keys_the_session_to_the_account_so_retype_login_and_stop_work()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var account = h.AddAccount("Storm Main");
        var process = StartOutside(h, 7500, age: TimeSpan.FromMinutes(1));
        var window = h.Windows.AddGameWindow(7500);
        h.Manager.ScanForExternalClients();
        var external = Assert.Single(h.Manager.Sessions);
        var published = new List<ClientSession>();
        h.Manager.SessionChanged += (_, session) => published.Add(session);

        Assert.Null(h.Manager.LinkExternal(external.AccountId, account.Id));

        var linked = Assert.Single(h.Manager.Sessions);
        Assert.Equal(account.Id, linked.AccountId);
        Assert.False(linked.IsExternal);
        Assert.Null(linked.Label);
        Assert.Equal(7500, linked.ProcessId);
        Assert.Equal(window, linked.WindowHandle);
        Assert.Null(h.Manager.Find(external.AccountId));
        Assert.Equal(linked, h.Manager.Find(account.Id));
        Assert.Collection(
            published,
            removed =>
            {
                Assert.Equal(external.AccountId, removed.AccountId);
                Assert.Equal(ClientSessionState.Exited, removed.State);
            },
            added => Assert.Equal(linked, added));

        // Now it is the account's client: recorded for a restart, its login can be retyped, and Stop closes it.
        Assert.Equal(account.Id, Assert.Single(ReadRunningClients(h)).AccountId);
        Assert.Null(await h.Time.RunUntilCompleteAsync(h.Manager.RetypeLoginAsync(account.Id, Ct)));
        Assert.Equal(4, h.Input.Log.Count);

        var exited = h.WaitForStateAsync(account.Id, ClientSessionState.Exited);
        Assert.True(h.Manager.Stop(account.Id));
        await exited;
        Assert.Equal(1, process.KillCount);
        Assert.Empty(h.Manager.Sessions);
        Assert.Empty(ReadRunningClients(h));
    }

    [Fact]
    public async Task Linking_refuses_accounts_that_are_running_missing_or_for_another_game()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var running = h.AddAccount("Running");
        var pirate = h.Accounts.Add("Pirate", game: GameKind.Pirate101);
        await h.Time.RunUntilCompleteAsync(h.Manager.LaunchAsync(running.Id, Ct));
        StartOutside(h, 7600, age: TimeSpan.FromMinutes(1));
        h.Windows.AddGameWindow(7600);
        h.Manager.ScanForExternalClients();
        var external = h.Manager.FindByProcessId(7600)!;

        Assert.Equal("Running already has a client running.", h.Manager.LinkExternal(external.AccountId, running.Id));
        Assert.Equal("This is a Wizard101 client, but Pirate is a Pirate101 account.", h.Manager.LinkExternal(external.AccountId, pirate.Id));
        Assert.Equal("This account no longer exists.", h.Manager.LinkExternal(external.AccountId, Guid.NewGuid()));
        Assert.Equal("This client isn't running any more.", h.Manager.LinkExternal(Guid.NewGuid(), pirate.Id));
        Assert.True(h.Manager.FindByProcessId(7600)!.IsExternal);
    }

    [Fact]
    public async Task Turning_detection_off_stops_adopting_and_lets_go_of_external_clients()
    {
        using var h = new SessionHarness(SessionHarness.FastLogin());
        var process = StartOutside(h, 7700, age: TimeSpan.FromMinutes(1));
        h.Windows.AddGameWindow(7700);
        h.Manager.ScanForExternalClients();
        var external = Assert.Single(h.Manager.Sessions);
        var released = h.WaitForStateAsync(external.AccountId, ClientSessionState.Exited);

        h.Settings.Update(settings => settings with { General = settings.General with { DetectExternalClients = false } });
        h.Manager.ScanForExternalClients();
        await released;

        Assert.Empty(h.Manager.Sessions);
        Assert.Equal(0, process.KillCount);
        Assert.Contains(7700, h.Audio.Released);

        StartOutside(h, 7701, age: TimeSpan.FromMinutes(1));
        h.Windows.AddGameWindow(7701);
        h.Time.Advance(TimeSpan.FromSeconds(10));
        h.Manager.ScanForExternalClients();

        Assert.Empty(h.Manager.Sessions);
    }

    [Fact]
    public void Detection_is_on_by_default()
    {
        Assert.True(new AppSettings().General.DetectExternalClients);
    }

    private static FakeLaunchedProcess StartOutside(SessionHarness h, int processId, TimeSpan age, GameKind game = GameKind.Wizard101)
    {
        var process = new FakeLaunchedProcess(processId)
        {
            StartTime = h.Time.GetUtcNow() - age,
            ProcessName = GameExecutables.ClientProcessName(game),
        };
        h.Launcher.AddRunning(process);
        return process;
    }

    private static IReadOnlyList<RunningClient> ReadRunningClients(SessionHarness h) =>
        JsonFileStore.Load(h.Paths.RunningClientsFile, CoreJsonContext.Default.RunningClientsDocument)?.Clients ?? [];
}
