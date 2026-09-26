namespace MultiWiz.Core.Sessions;

/// <summary>Launches game clients, logs them in, and tracks them until they exit. Thread-safe.</summary>
public interface ISessionManager
{
    /// <summary>Snapshot of sessions that are alive (see <see cref="ClientSession.IsAlive"/>).</summary>
    IReadOnlyList<ClientSession> Sessions { get; }

    ClientSession? Find(Guid accountId);
    ClientSession? FindByProcessId(int processId);
    ClientSession? FindByWindow(nint windowHandle);

    /// <summary>
    /// Starts the account's client (no-op returning the existing session if it is already alive) and, if
    /// auto-login is on, types its credentials. Completes when the session reaches Running, Failed, or Exited.
    /// Never throws for launch/login problems; they are reported as a Failed session.
    /// Client processes start at least <see cref="Settings.LoginSettings.StaggerSeconds"/> apart, across all launches.
    /// </summary>
    Task<ClientSession> LaunchAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Launches several accounts, starting them <see cref="Settings.LoginSettings.StaggerSeconds"/> apart.
    /// Logins are typed one client at a time. Completes when every launch completes.
    /// </summary>
    Task<IReadOnlyList<ClientSession>> LaunchManyAsync(IReadOnlyList<Guid> accountIds, CancellationToken cancellationToken = default);

    /// <summary>Kills the client for the account. Returns false if nothing was running.</summary>
    bool Stop(Guid accountId);

    void StopAll();

    /// <summary>Raised with the new snapshot whenever a session changes state, on an arbitrary thread.</summary>
    event EventHandler<ClientSession>? SessionChanged;
}
