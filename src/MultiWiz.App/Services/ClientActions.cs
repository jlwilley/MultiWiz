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
    private readonly StatusService _status;
    private readonly ILogger<ClientActions> _logger;

    public ClientActions(
        ISessionManager sessions,
        ITeamLauncher teamLauncher,
        ITeamStore teams,
        IAccountStore accounts,
        ISettingsStore settings,
        WindowCoordinator windows,
        StatusService status,
        ILogger<ClientActions> logger)
    {
        _sessions = sessions;
        _teamLauncher = teamLauncher;
        _teams = teams;
        _accounts = accounts;
        _settings = settings;
        _windows = windows;
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
        Observe(LaunchManyCoreAsync(accountIds), "launch");
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
        Observe(LaunchTeamCoreAsync(team), "team launch");
    }

    public bool Stop(Guid accountId) => _sessions.Stop(accountId);

    public void StopAll()
    {
        var running = _sessions.Sessions.Count;
        _sessions.StopAll();
        if (running > 0)
        {
            _status.Show(running == 1 ? "Stopped 1 client." : $"Stopped {running} clients.");
        }
    }

    private async Task LaunchManyCoreAsync(IReadOnlyList<Guid> accountIds)
    {
        var results = await _sessions.LaunchManyAsync(accountIds).ConfigureAwait(false);
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
