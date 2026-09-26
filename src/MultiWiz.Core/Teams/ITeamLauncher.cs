using MultiWiz.Core.Sessions;

namespace MultiWiz.Core.Teams;

public interface IWindowArranger
{
    /// <summary>Places the sessions' windows (in order) using the layout. Returns how many windows were moved.</summary>
    int Arrange(IReadOnlyList<ClientSession> orderedSessions, WindowLayout layout, bool resize);
}

public interface ITeamLauncher
{
    /// <summary>Makes the team active in the switcher, launches its accounts that aren't running, then arranges its windows.</summary>
    Task<IReadOnlyList<ClientSession>> LaunchAsync(Guid teamId, CancellationToken cancellationToken = default);

    /// <summary>Arranges the team's currently running windows with its layout. Returns how many were moved.</summary>
    int ArrangeNow(Guid teamId);
}
