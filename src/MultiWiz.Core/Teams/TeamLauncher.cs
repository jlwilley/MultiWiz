using Microsoft.Extensions.Logging;
using MultiWiz.Core.Sessions;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Switching;

namespace MultiWiz.Core.Teams;

/// <summary>Launches and arranges whole teams.</summary>
public sealed class TeamLauncher : ITeamLauncher
{
    private readonly ITeamStore _teams;
    private readonly ISessionManager _sessions;
    private readonly IClientSwitcher _switcher;
    private readonly IWindowArranger _arranger;
    private readonly ISettingsStore _settings;
    private readonly ILogger<TeamLauncher> _logger;

    public TeamLauncher(
        ITeamStore teams,
        ISessionManager sessions,
        IClientSwitcher switcher,
        IWindowArranger arranger,
        ISettingsStore settings,
        ILogger<TeamLauncher> logger)
    {
        _teams = teams;
        _sessions = sessions;
        _switcher = switcher;
        _arranger = arranger;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Returns one session per team account in slot order: the live session for accounts that are running, or the
    /// final (failed or exited) session for launches that did not get there. Empty if the team does not exist.
    /// </summary>
    public async Task<IReadOnlyList<ClientSession>> LaunchAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        // Callers start this from the UI thread. Without this yield, a team whose clients are all running would be
        // arranged there synchronously, and restoring a minimized or maximized window waits for that game's UI thread,
        // so a busy client would freeze MultiWiz. The start of each launch (install lookup, Steam, Process.Start) and
        // the settings writes below also stay off the UI thread this way.
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        var team = _teams.Find(teamId);
        if (team is null)
        {
            _logger.LogWarning("Team {TeamId} does not exist", teamId);
            return [];
        }

        _switcher.SetActiveTeam(team.Id);

        var accountIds = team.AccountIds.Distinct().ToArray();
        var toLaunch = accountIds.Where(id => _sessions.Find(id) is not { IsAlive: true }).ToArray();
        _logger.LogInformation(
            "Launching team {TeamName}: {LaunchCount} of {AccountCount} accounts need starting",
            team.Name, toLaunch.Length, accountIds.Length);

        IReadOnlyList<ClientSession> launched = toLaunch.Length == 0
            ? []
            : await _sessions.LaunchManyAsync(toLaunch, cancellationToken).ConfigureAwait(false);

        var results = new List<ClientSession>(accountIds.Length);
        foreach (var accountId in accountIds)
        {
            var session = _sessions.Find(accountId) ?? launched.FirstOrDefault(s => s.AccountId == accountId);
            if (session is not null)
            {
                results.Add(session);
            }
        }

        // The clients are running at this point, so arranging and remembering the team are best effort: a failure
        // there must not turn the launch into an error and lose the sessions.
        try
        {
            if (ResolveLayout(team) is { } layout)
            {
                _arranger.Arrange(AliveSessions(accountIds), layout, team.ResizeWindows);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Arranging the windows of team {TeamName} failed", team.Name);
        }

        try
        {
            _settings.Update(settings => settings.LastTeamId == team.Id ? settings : settings with { LastTeamId = team.Id });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Saving the last launched team failed");
        }

        return results;
    }

    public int ArrangeNow(Guid teamId)
    {
        var team = _teams.Find(teamId);
        if (team is null || ResolveLayout(team) is not { } layout)
        {
            return 0;
        }

        return _arranger.Arrange(AliveSessions(team.AccountIds.Distinct()), layout, team.ResizeWindows);
    }

    private ClientSession[] AliveSessions(IEnumerable<Guid> accountIds) =>
        accountIds.Select(id => _sessions.Find(id)).OfType<ClientSession>().Where(session => session.IsAlive).ToArray();

    private WindowLayout? ResolveLayout(Team team)
    {
        if (string.Equals(team.LayoutId, BuiltInLayouts.NoneId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var layout = BuiltInLayouts.Find(team.LayoutId);
        if (layout is null)
        {
            _logger.LogWarning("Team {TeamName} uses unknown layout {LayoutId}; windows were not arranged", team.Name, team.LayoutId);
            return null;
        }

        return layout.Cells.Count > 0 ? layout : null;
    }
}
