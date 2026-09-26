using Microsoft.Extensions.Logging.Abstractions;
using MultiWiz.Core.Primitives;
using MultiWiz.Core.Sessions;
using MultiWiz.Core.Teams;
using MultiWiz.Core.Tests.Fakes;

namespace MultiWiz.Core.Tests;

public sealed class WindowArrangerTests
{
    private readonly FakeDisplayService _display = new();
    private readonly FakeWindowService _windows = new();

    [Fact]
    public void Places_each_window_in_its_cell_and_skips_sessions_without_a_window()
    {
        _display.Monitors.Add(FakeDisplayService.Monitor(0, 0, 0, 1920, 1080));
        var arranger = new WindowArranger(_display, _windows, NullLogger<WindowArranger>.Instance);
        var first = FakeSessionManager.Running(Guid.NewGuid(), 101);
        var windowless = FakeSessionManager.Running(Guid.NewGuid(), 102, withWindow: false);
        var third = FakeSessionManager.Running(Guid.NewGuid(), 103);

        var placed = arranger.Arrange([first, windowless, third], BuiltInLayouts.Grid2x2, resize: false);

        Assert.Equal(2, placed);
        Assert.Equal(
            new[]
            {
                (first.WindowHandle, new PixelRect(0, 0, 960, 520), false),
                (third.WindowHandle, new PixelRect(0, 520, 960, 520), false),
            },
            _windows.SetBoundsCalls.ToArray());
    }

    [Fact]
    public void Does_nothing_without_cells_or_monitors()
    {
        var arranger = new WindowArranger(_display, _windows, NullLogger<WindowArranger>.Instance);
        var session = FakeSessionManager.Running(Guid.NewGuid(), 101);

        Assert.Equal(0, arranger.Arrange([session], BuiltInLayouts.SideBySide, resize: true));

        _display.Monitors.Add(FakeDisplayService.Monitor(0, 0, 0, 1920, 1080));
        Assert.Equal(0, arranger.Arrange([session], BuiltInLayouts.None, resize: true));
        Assert.Empty(_windows.SetBoundsCalls);
    }
}

public sealed class TeamLauncherTests
{
    private readonly FakeTeamStore _teams = new();
    private readonly FakeSessionManager _sessions = new();
    private readonly FakeClientSwitcher _switcher = new();
    private readonly FakeDisplayService _display = new();
    private readonly FakeWindowService _windows = new();
    private readonly FakeSettingsStore _settings = new();
    private readonly TeamLauncher _launcher;

    public TeamLauncherTests()
    {
        _display.Monitors.Add(FakeDisplayService.Monitor(0, 0, 0, 1920, 1080));
        var arranger = new WindowArranger(_display, _windows, NullLogger<WindowArranger>.Instance);
        _launcher = new TeamLauncher(_teams, _sessions, _switcher, arranger, _settings, NullLogger<TeamLauncher>.Instance);
    }

    [Fact]
    public async Task Launches_missing_accounts_arranges_the_team_and_remembers_it()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        var alreadyRunning = _sessions.Start(b);
        var team = _teams.Add("Farm", [a, b, c, a], BuiltInLayouts.SideBySide.Id, resizeWindows: false);

        var sessions = await _launcher.LaunchAsync(team.Id, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { $"team:{team.Id}" }, _switcher.Calls);
        Assert.Equal(new[] { a, c }, Assert.Single(_sessions.LaunchManyCalls).ToArray());
        Assert.Equal(new[] { a, b, c }, sessions.Select(session => session.AccountId).ToArray());
        Assert.Equal(alreadyRunning, sessions[1]);
        Assert.All(sessions, session => Assert.Equal(ClientSessionState.Running, session.State));

        // Slot order a, b, c over two side-by-side cells: a left, b right, c left again.
        var calls = _windows.SetBoundsCalls;
        Assert.Equal(3, calls.Count);
        Assert.Equal(sessions[0].WindowHandle, calls[0].Window);
        Assert.Equal(0, calls[0].Bounds.X);
        Assert.Equal(960, calls[1].Bounds.X);
        Assert.Equal(0, calls[2].Bounds.X);
        Assert.All(calls, call => Assert.False(call.Resize));

        Assert.Equal(team.Id, _settings.Current.LastTeamId);
    }

    [Fact]
    public async Task Does_not_relaunch_or_move_windows_for_a_running_team_without_a_layout()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        _sessions.Start(a);
        _sessions.Start(b);
        var team = _teams.Add("Idle", [a, b]);

        var sessions = await _launcher.LaunchAsync(team.Id, TestContext.Current.CancellationToken);

        Assert.Equal(2, sessions.Count);
        Assert.Empty(_sessions.LaunchManyCalls);
        Assert.Empty(_windows.SetBoundsCalls);
        Assert.Equal(team.Id, _settings.Current.LastTeamId);
    }

    [Fact]
    public async Task Unknown_team_does_nothing()
    {
        var sessions = await _launcher.LaunchAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Empty(sessions);
        Assert.Empty(_switcher.Calls);
        Assert.Equal(0, _settings.UpdateCount);
        Assert.Equal(0, _launcher.ArrangeNow(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_failure_to_arrange_or_save_after_launching_still_returns_the_sessions()
    {
        var launcher = new TeamLauncher(_teams, _sessions, _switcher, new ThrowingArranger(), _settings, NullLogger<TeamLauncher>.Instance);
        _settings.UpdateException = new IOException("settings.json is locked.");
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        var team = _teams.Add("Unlucky", [a, b], BuiltInLayouts.SideBySide.Id);

        var sessions = await launcher.LaunchAsync(team.Id, TestContext.Current.CancellationToken);

        Assert.Equal(new[] { a, b }, sessions.Select(session => session.AccountId).ToArray());
        Assert.All(sessions, session => Assert.Equal(ClientSessionState.Running, session.State));
    }

    [Fact]
    public void ArrangeNow_moves_only_the_running_windows_of_the_team()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), stranger = Guid.NewGuid();
        _sessions.Start(a);
        _sessions.Start(stranger);
        var team = _teams.Add("Half", [a, b], BuiltInLayouts.Grid2x2.Id);

        var moved = _launcher.ArrangeNow(team.Id);

        Assert.Equal(1, moved);
        var call = Assert.Single(_windows.SetBoundsCalls);
        Assert.Equal(_sessions.Find(a)!.WindowHandle, call.Window);
        Assert.True(call.Resize);
        Assert.Empty(_sessions.LaunchManyCalls);
    }

    [Fact]
    public void ArrangeNow_ignores_unknown_layouts()
    {
        var a = Guid.NewGuid();
        _sessions.Start(a);
        var team = _teams.Add("Odd", [a], "no-such-layout");

        Assert.Equal(0, _launcher.ArrangeNow(team.Id));
        Assert.Empty(_windows.SetBoundsCalls);
    }

    [Fact]
    public async Task Arranging_a_team_that_is_already_running_does_not_block_the_caller()
    {
        using var release = new ManualResetEventSlim();
        var arranger = new BlockingArranger(release);
        var launcher = new TeamLauncher(_teams, _sessions, _switcher, arranger, _settings, NullLogger<TeamLauncher>.Instance);
        var a = Guid.NewGuid();
        _sessions.Start(a);
        var team = _teams.Add("Running", [a], BuiltInLayouts.SideBySide.Id);

        // Like restoring a minimized client whose UI thread is busy: arranging waits until the game answers. The caller
        // (the UI thread in the app) must get control back right away.
        var launch = launcher.LaunchAsync(team.Id, TestContext.Current.CancellationToken);
        Assert.False(launch.IsCompleted);

        release.Set();
        var sessions = await launch;

        Assert.Equal(a, Assert.Single(sessions).AccountId);
        Assert.Equal(1, arranger.Calls);
    }

    private sealed class BlockingArranger(ManualResetEventSlim release) : IWindowArranger
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public int Arrange(IReadOnlyList<ClientSession> orderedSessions, WindowLayout layout, bool resize)
        {
            Interlocked.Increment(ref _calls);
            release.Wait(TimeSpan.FromSeconds(30));
            return orderedSessions.Count;
        }
    }

    private sealed class ThrowingArranger : IWindowArranger
    {
        public int Arrange(IReadOnlyList<ClientSession> orderedSessions, WindowLayout layout, bool resize) =>
            throw new InvalidOperationException("The display configuration changed.");
    }
}
