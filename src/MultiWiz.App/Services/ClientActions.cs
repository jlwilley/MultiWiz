using Microsoft.Extensions.Logging;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Sessions;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Teams;

namespace MultiWiz.App.Services;

/// <summary>
/// Launch/stop actions shared by the account list, the team editor and the tray menu. Launches run in the
/// background; their outcome is reported through <see cref="StatusService"/>. Call from the UI thread.
/// </summary>
public sealed class ClientActions
{
    private readonly ISessionManager _sessions;
    private readonly ITeamLauncher _teamLauncher;
    private readonly ITeamStore _teams;
    private readonly IAccountStore _accounts;
    private readonly ISettingsStore _settings;
    private readonly WindowCoordinator _windows;
    private readonly IDialogService _dialogs;
    private readonly StatusService _status;
    private readonly ILogger<ClientActions> _logger;
    private static readonly TimeSpan DoubleClickGuard = TimeSpan.FromSeconds(2);
    private bool _confirmingExit;

    // The synchronous start of the latest LaunchMany call. Each call's start runs after the previous one, so clients
    // are started (and staggered) in the order they were asked for, as when launches started on the UI thread.
    private Task _launchStarts = Task.CompletedTask;

    public ClientActions(
        ISessionManager sessions,
        ITeamLauncher teamLauncher,
        ITeamStore teams,
        IAccountStore accounts,
        ISettingsStore settings,
        WindowCoordinator windows,
        IDialogService dialogs,
        StatusService status,
        ILogger<ClientActions> logger)
    {
        _sessions = sessions;
        _teamLauncher = teamLauncher;
        _teams = teams;
        _accounts = accounts;
        _settings = settings;
        _windows = windows;
        _dialogs = dialogs;
        _status = status;
        _logger = logger;
    }

    public void Launch(Guid accountId) => LaunchMany([accountId]);

    public void LaunchMany(IReadOnlyList<Guid> accountIds)
    {
        if (accountIds.Count == 0)
        {
            return;
        }

        _status.Show(accountIds.Count == 1
            ? $"Launching {AccountName(accountIds[0])}…"
            : $"Launching {accountIds.Count} accounts…");
        // Off the UI thread: the start of a launch (install lookup, Steam, Process.Start) runs before its first await.
        var started = _launchStarts.ContinueWith(
            _ => _sessions.LaunchManyAsync(accountIds),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);
        _launchStarts = started;
        Observe(LaunchManyCoreAsync(started.Unwrap()), "launch");
    }

    public void LaunchTeam(Guid teamId)
    {
        var team = _teams.Find(teamId);
        if (team is null)
        {
            return;
        }

        if (team.AccountIds.Count == 0)
        {
            _status.Show($"{team.Name} has no accounts yet. Add some on the Teams page.", isError: true);
            return;
        }

        if (_settings.Current.Switcher.ShowOnTeamLaunch)
        {
            _windows.ShowSwitcher();
        }

        _status.Show($"Launching {team.Name}…");
        // Off the UI thread: arranging an already running team moves other processes' windows, which waits for them.
        Observe(Task.Run(() => LaunchTeamCoreAsync(team)), "team launch");
    }

    /// <summary>
    /// Stops the account's client. A Stop within the first couple of seconds of a launch is ignored: the Stop button
    /// appears where Launch was, so a double-click on Launch would otherwise close the client it just started.
    /// </summary>
    public bool Stop(Guid accountId)
    {
        if (_sessions.Find(accountId) is { State: ClientSessionState.Launching or ClientSessionState.WaitingForWindow } session
            && DateTimeOffset.UtcNow - session.StartedAt < DoubleClickGuard)
        {
            _status.Show($"{AccountName(accountId)} is still starting. Click Stop again in a moment to close it.");
            return false;
        }

        return _sessions.Stop(accountId);
    }

    /// <summary>Stops the account's client right away (used when its account is deleted).</summary>
    public bool StopNow(Guid accountId) => _sessions.Stop(accountId);

    /// <summary>
    /// Before MultiWiz quits or restarts: when game clients are running, asks whether to go ahead, saying what happens
    /// to them (closed with "Close games on exit", otherwise left running without MultiWiz). True to go ahead.
    /// </summary>
    public async Task<bool> ConfirmExitAsync(string actionText)
    {
        var running = _sessions.Sessions.Count(session => session.IsAlive);
        if (running == 0)
        {
            return true;
        }

        if (_confirmingExit)
        {
            return false; // The question is already on screen.
        }

        _confirmingExit = true;
        try
        {
            var closesGames = _settings.Current.General.CloseGamesOnExit;
            var clients = running == 1 ? "1 game client is" : $"{running} game clients are";
            return await _dialogs.ConfirmAsync(
                $"{actionText}?",
                closesGames
                    ? $"{clients} running and will be closed (Settings → General → \"Close game clients when MultiWiz exits\")."
                    : $"{clients} running. They keep running, but without MultiWiz's hotkeys, audio switching and " +
                      "name badges while MultiWiz is closed.",
                actionText,
                isDestructive: closesGames);
        }
        finally
        {
            _confirmingExit = false;
        }
    }

    public void StopAll()
    {
        var running = _sessions.Sessions.Count;
        _sessions.StopAll();
        if (running > 0)
        {
            _status.Show(running == 1 ? "Stopped 1 client." : $"Stopped {running} clients.");
        }
    }

    private async Task LaunchManyCoreAsync(Task<IReadOnlyList<ClientSession>> launch)
    {
        var results = await launch.ConfigureAwait(false);
        Report(results, successMessage: null);
    }

    private async Task LaunchTeamCoreAsync(Team team)
    {
        var results = await _teamLauncher.LaunchAsync(team.Id).ConfigureAwait(false);
        Report(results, $"{team.Name} is ready.");
    }

    private void Report(IReadOnlyList<ClientSession> results, string? successMessage)
    {
        var failures = results.Where(session => session.State == ClientSessionState.Failed).ToArray();
        if (failures.Length == 1)
        {
            var failure = failures[0];
            _status.Show($"{AccountName(failure.AccountId)}: {failure.Error ?? "the launch failed."}", isError: true);
        }
        else if (failures.Length > 1)
        {
            _status.Show($"{failures.Length} clients failed to start. {failures[0].Error}", isError: true);
        }
        else if (successMessage is not null)
        {
            _status.Show(successMessage);
        }
    }

    private string AccountName(Guid accountId) => _accounts.Find(accountId)?.DisplayName ?? "An account";

    private void Observe(Task task, string operation) =>
        task.ContinueWith(
            completed =>
            {
                _logger.LogError(completed.Exception, "Unexpected error during {Operation}", operation);
                _status.Show($"Something went wrong during the {operation}. See the log for details.", isError: true);
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
}
