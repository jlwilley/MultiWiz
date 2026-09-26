using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Sessions;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Switching;
using MultiWiz.Core.Tests.Fakes;

namespace MultiWiz.Core.Tests;

public sealed class ClientSwitcherTests : IDisposable
{
    private static readonly TimeSpan PastDebounce = TimeSpan.FromMilliseconds(150);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeSessionManager _sessions = new();
    private readonly FakeTeamStore _teams = new();
    private readonly FakeAccountStore _accounts = new();
    private readonly FakeSettingsStore _settings;
    private readonly FakeWindowService _windows = new();
    private readonly FakeWindowEvents _windowEvents = new();
    private readonly FakeAudioService _audio = new();
    private readonly FakeProcessThrottler _throttler = new();
    private readonly ClientSwitcher _switcher;
    private readonly Account _alpha;
    private readonly Account _bravo;
    private readonly Account _charlie;

    public ClientSwitcherTests()
    {
        _settings = new FakeSettingsStore(new AppSettings { Audio = new AudioSettings { Enabled = true, FocusedVolumePercent = 90, UnfocusedVolumePercent = 10 } });
        _alpha = _accounts.Add("Alpha");
        _bravo = _accounts.Add("Bravo");
        _charlie = _accounts.Add("Charlie");
        _switcher = new ClientSwitcher(
            _sessions, _teams, _accounts, _settings, _windows, _windowEvents, _audio, _throttler, _time, NullLogger<ClientSwitcher>.Instance);
    }

    public void Dispose() => _switcher.Dispose();

    [Fact]
    public void Orders_running_clients_by_account_order_when_no_team_is_active()
    {
        _sessions.Start(_charlie.Id);
        _sessions.Start(_alpha.Id);
        _sessions.Start(_bravo.Id);

        Assert.Equal(new[] { _alpha.Id, _bravo.Id, _charlie.Id }, OrderedAccountIds());
    }

    [Fact]
    public void Active_team_slots_come_first_then_other_clients()
    {
        StartAll();
        var team = _teams.Add("Farm", [_charlie.Id, _alpha.Id]);
        var changes = 0;
        _switcher.Changed += (_, _) => changes++;

        _switcher.SetActiveTeam(team.Id);

        Assert.Equal(team.Id, _switcher.ActiveTeamId);
        Assert.Equal(new[] { _charlie.Id, _alpha.Id, _bravo.Id }, OrderedAccountIds());
        Assert.True(changes > 0);
    }

    [Fact]
    public void Clients_without_a_window_are_not_switchable()
    {
        _sessions.Start(_alpha.Id);
        _sessions.Start(_bravo.Id, withWindow: false);

        Assert.Equal(new[] { _alpha.Id }, OrderedAccountIds());
    }

    [Fact]
    public void Deleting_the_active_team_falls_back_to_account_order()
    {
        StartAll();
        var team = _teams.Add("Gone", [_charlie.Id]);
        _switcher.SetActiveTeam(team.Id);

        _teams.Remove(team.Id);

        Assert.Null(_switcher.ActiveTeamId);
        Assert.Equal(new[] { _alpha.Id, _bravo.Id, _charlie.Id }, OrderedAccountIds());
    }

    [Fact]
    public void FocusSlot_focuses_the_window_in_that_slot_and_makes_it_current()
    {
        StartAll();

        Assert.True(_switcher.FocusSlot(1));

        Assert.Equal(WindowOf(_bravo), Assert.Single(_windows.FocusCalls));
        Assert.Equal(_bravo.Id, _switcher.Current?.AccountId);
    }

    [Fact]
    public void FocusSlot_outside_the_list_does_nothing()
    {
        StartAll();

        Assert.False(_switcher.FocusSlot(3));
        Assert.False(_switcher.FocusSlot(-1));
        Assert.Empty(_windows.FocusCalls);
    }

    [Fact]
    public void A_failed_focus_does_not_change_current()
    {
        StartAll();
        _windows.FocusSucceeds = false;

        Assert.False(_switcher.FocusSlot(0));
        Assert.Null(_switcher.Current);
    }

    [Fact]
    public void FocusNext_starts_at_the_first_slot_and_wraps_around()
    {
        StartAll();

        Assert.True(_switcher.FocusNext());
        Assert.Equal(_alpha.Id, _switcher.Current?.AccountId);
        Assert.True(_switcher.FocusNext());
        Assert.Equal(_bravo.Id, _switcher.Current?.AccountId);
        Assert.True(_switcher.FocusNext());
        Assert.Equal(_charlie.Id, _switcher.Current?.AccountId);
        Assert.True(_switcher.FocusNext());
        Assert.Equal(_alpha.Id, _switcher.Current?.AccountId);
    }

    [Fact]
    public void FocusPrevious_starts_at_the_last_slot_and_wraps_around()
    {
        StartAll();

        Assert.True(_switcher.FocusPrevious());
        Assert.Equal(_charlie.Id, _switcher.Current?.AccountId);
        Assert.True(_switcher.FocusPrevious());
        Assert.Equal(_bravo.Id, _switcher.Current?.AccountId);

        Assert.True(_switcher.FocusSlot(0));
        Assert.True(_switcher.FocusPrevious());
        Assert.Equal(_charlie.Id, _switcher.Current?.AccountId);
    }

    [Fact]
    public void Next_and_previous_do_nothing_without_clients()
    {
        Assert.False(_switcher.FocusNext());
        Assert.False(_switcher.FocusPrevious());
        Assert.Empty(_windows.FocusCalls);
    }

    [Fact]
    public void Focus_by_account_focuses_that_client()
    {
        StartAll();

        Assert.True(_switcher.Focus(_charlie.Id));
        Assert.False(_switcher.Focus(Guid.NewGuid()));

        Assert.Equal(WindowOf(_charlie), Assert.Single(_windows.FocusCalls));
    }

    [Fact]
    public void Tracks_foreground_changes_made_outside_multiwiz()
    {
        StartAll();
        var bravo = _sessions.Find(_bravo.Id)!;

        _windowEvents.RaiseForeground(bravo.WindowHandle, bravo.ProcessId);
        Assert.Equal(_bravo.Id, _switcher.Current?.AccountId);

        // A non-game window (e.g. a browser) does not change the current client.
        _windowEvents.RaiseForeground(123456, 99999);
        Assert.Equal(_bravo.Id, _switcher.Current?.AccountId);

        // A secondary window of a game process counts as that game.
        var charlie = _sessions.Find(_charlie.Id)!;
        _windowEvents.RaiseForeground(777, charlie.ProcessId);
        Assert.Equal(_charlie.Id, _switcher.Current?.AccountId);

        // Tracking foreground changes never moves focus itself.
        Assert.Empty(_windows.FocusCalls);
    }

    [Fact]
    public void Current_is_cleared_when_that_client_exits()
    {
        StartAll();
        _switcher.FocusSlot(0);
        var alpha = _sessions.Find(_alpha.Id)!;

        _sessions.Set(alpha with { State = ClientSessionState.Exited });

        Assert.Null(_switcher.Current);
        Assert.Equal(new[] { _bravo.Id, _charlie.Id }, OrderedAccountIds());
    }

    [Fact]
    public void A_client_focused_before_its_window_is_recorded_stays_current_when_other_clients_change()
    {
        _sessions.Start(_alpha.Id);
        var bravo = _sessions.Start(_bravo.Id, withWindow: false);
        var bravoWindow = FakeWindowService.WindowFor(bravo.ProcessId);

        // Bravo's window takes the foreground before the session manager has recorded it.
        _windowEvents.RaiseForeground(bravoWindow, bravo.ProcessId);
        Assert.Null(_switcher.Current);

        // Another client changes state in the meantime, which rebuilds the order.
        _sessions.Start(_charlie.Id);
        SettleEffects();

        // Bravo's window is recorded: it becomes the current client and gets the focused volume.
        _sessions.Set(bravo with { WindowHandle = bravoWindow });
        Assert.Equal(_bravo.Id, _switcher.Current?.AccountId);

        _time.Advance(PastDebounce);
        var targets = _audio.Applied.Last();
        Assert.Equal(90, VolumeOf(targets, _bravo));
        Assert.Equal(10, VolumeOf(targets, _alpha));
        Assert.Equal(10, VolumeOf(targets, _charlie));
    }

    [Fact]
    public void Focus_changes_apply_audio_after_the_debounce()
    {
        StartAll();
        SettleEffects();

        _switcher.FocusSlot(1);
        _time.Advance(FocusEffects.Debounce - TimeSpan.FromMilliseconds(1));
        Assert.Empty(_audio.Applied);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        var targets = Assert.Single(_audio.Applied);
        Assert.Equal(90, VolumeOf(targets, _bravo));
        Assert.Equal(10, VolumeOf(targets, _alpha));
        Assert.Equal(10, VolumeOf(targets, _charlie));
    }

    [Fact]
    public void Rapid_switching_applies_only_the_final_state()
    {
        StartAll();
        SettleEffects();

        _switcher.FocusSlot(0);
        _time.Advance(TimeSpan.FromMilliseconds(50));
        _switcher.FocusSlot(1);
        _time.Advance(TimeSpan.FromMilliseconds(50));
        _switcher.FocusSlot(2);
        _time.Advance(PastDebounce);

        var targets = Assert.Single(_audio.Applied);
        Assert.Equal(90, VolumeOf(targets, _charlie));
        Assert.Equal(10, VolumeOf(targets, _alpha));
    }

    [Fact]
    public void Turning_audio_off_restores_volumes_once()
    {
        StartAll();
        _switcher.FocusSlot(0);
        SettleEffects();

        _settings.Update(settings => settings with { Audio = settings.Audio with { Enabled = false } });
        _time.Advance(PastDebounce);
        _switcher.FocusSlot(1);
        _time.Advance(PastDebounce);

        Assert.Equal(1, _audio.RestoreAllCount);
        Assert.Empty(_audio.Applied);
    }

    [Fact]
    public void Performance_options_throttle_background_clients_and_release_them_when_turned_off()
    {
        StartAll();
        _switcher.FocusSlot(0);
        SettleEffects();
        Assert.Empty(_throttler.Applied);

        _settings.Update(settings => settings with { Performance = new PerformanceSettings { EfficiencyModeForBackground = true } });
        _time.Advance(PastDebounce);

        var applied = _throttler.Applied;
        Assert.Equal(3, applied.Count);
        Assert.Contains(applied, call => call.ProcessId == ProcessOf(_alpha) && !call.Background && call.EfficiencyMode && !call.LowerPriority);
        Assert.Contains(applied, call => call.ProcessId == ProcessOf(_bravo) && call.Background);
        Assert.Contains(applied, call => call.ProcessId == ProcessOf(_charlie) && call.Background);

        _throttler.Clear();
        _switcher.FocusSlot(1);
        _time.Advance(PastDebounce);

        // Only the two clients whose role changed are touched.
        Assert.Equal(2, _throttler.Applied.Count);
        Assert.Contains(_throttler.Applied, call => call.ProcessId == ProcessOf(_bravo) && !call.Background);
        Assert.Contains(_throttler.Applied, call => call.ProcessId == ProcessOf(_alpha) && call.Background);

        _settings.Update(settings => settings with { Performance = new PerformanceSettings() });
        _time.Advance(PastDebounce);

        Assert.Equal(
            new[] { ProcessOf(_alpha), ProcessOf(_bravo), ProcessOf(_charlie) }.Order().ToArray(),
            _throttler.Released.Order().ToArray());
    }

    [Fact]
    public void Clients_that_are_still_logging_in_are_not_throttled_until_they_are_running()
    {
        _sessions.Start(_alpha.Id);
        _sessions.Start(_bravo.Id);
        var loading = FakeSessionManager.Running(_charlie.Id, 900) with { State = ClientSessionState.LoggingIn };
        _sessions.Set(loading);
        _switcher.FocusSlot(0);
        _settings.Update(settings => settings with { Performance = new PerformanceSettings { LowerBackgroundPriority = true } });
        _time.Advance(PastDebounce);

        Assert.Equal(2, _throttler.Applied.Count);
        Assert.DoesNotContain(_throttler.Applied, call => call.ProcessId == loading.ProcessId);

        _sessions.Set(loading with { State = ClientSessionState.Running });
        _time.Advance(PastDebounce);

        Assert.Contains(_throttler.Applied, call => call.ProcessId == loading.ProcessId && call.Background && call.LowerPriority);
    }

    [Fact]
    public void The_active_team_is_remembered_in_settings()
    {
        var team = _teams.Add("Farm", [_bravo.Id]);

        _switcher.SetActiveTeam(team.Id);
        Assert.Equal(team.Id, _settings.Current.LastTeamId);

        _switcher.SetActiveTeam(null);
        Assert.Null(_settings.Current.LastTeamId);
    }

    [Fact]
    public void Starts_with_the_last_launched_team_active()
    {
        var team = _teams.Add("Saved", [_bravo.Id]);
        _settings.Update(settings => settings with { LastTeamId = team.Id });
        StartAll();

        using var switcher = new ClientSwitcher(
            _sessions, _teams, _accounts, _settings, _windows, _windowEvents, _audio, _throttler, _time, NullLogger<ClientSwitcher>.Instance);

        Assert.Equal(team.Id, switcher.ActiveTeamId);
        Assert.Equal(_bravo.Id, switcher.OrderedSessions[0].AccountId);
    }

    [Fact]
    public void Clients_started_outside_MultiWiz_come_after_account_clients_ordered_by_label()
    {
        var team = _teams.Add("Duo", [_bravo.Id]);
        _switcher.SetActiveTeam(team.Id);
        var tenth = External("Wizard101 client 10", 910);
        var second = External("Wizard101 client 2", 920);
        var pirate = External("Pirate101 client 1", 930);
        _sessions.Start(_charlie.Id);
        _sessions.Start(_bravo.Id);
        _sessions.Start(_alpha.Id);

        Assert.Equal(
            new[] { _bravo.Id, _alpha.Id, _charlie.Id, pirate.AccountId, second.AccountId, tenth.AccountId },
            OrderedAccountIds());

        // They are switchable like any other client.
        Assert.True(_switcher.FocusSlot(4));
        Assert.Equal(second.AccountId, _switcher.Current?.AccountId);
        Assert.Contains(WindowFor(920), _windows.FocusCalls);
    }

    private ClientSession External(string label, int processId)
    {
        var session = FakeSessionManager.Running(Guid.NewGuid(), processId) with { IsExternal = true, Label = label };
        _sessions.Set(session);
        return session;
    }

    private static nint WindowFor(int processId) => FakeWindowService.WindowFor(processId);

    private void StartAll()
    {
        _sessions.Start(_alpha.Id);
        _sessions.Start(_bravo.Id);
        _sessions.Start(_charlie.Id);
    }

    // Lets the effects scheduled by setup run, then forgets them.
    private void SettleEffects()
    {
        _time.Advance(PastDebounce);
        _audio.Clear();
        _throttler.Clear();
    }

    private Guid[] OrderedAccountIds() => _switcher.OrderedSessions.Select(session => session.AccountId).ToArray();

    private nint WindowOf(Account account) => _sessions.Find(account.Id)!.WindowHandle;

    private int ProcessOf(Account account) => _sessions.Find(account.Id)!.ProcessId;

    private int VolumeOf(IReadOnlyList<VolumeTarget> targets, Account account) =>
        targets.Single(target => target.ProcessId == ProcessOf(account)).VolumePercent;
}
