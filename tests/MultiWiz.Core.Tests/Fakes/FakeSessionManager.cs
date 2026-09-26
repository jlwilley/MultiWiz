using MultiWiz.Core.Sessions;

namespace MultiWiz.Core.Tests.Fakes;

/// <summary>An in-memory session list the test controls directly; launches create Running sessions immediately.</summary>
internal sealed class FakeSessionManager : ISessionManager
{
    private readonly Lock _lock = new();
    private readonly List<ClientSession> _sessions = [];
    private readonly List<IReadOnlyList<Guid>> _launchManyCalls = [];
    private int _nextProcessId = 500;

    public event EventHandler<ClientSession>? SessionChanged;

    public IReadOnlyList<IReadOnlyList<Guid>> LaunchManyCalls
    {
        get
        {
            lock (_lock)
            {
                return _launchManyCalls.ToArray();
            }
        }
    }

    public IReadOnlyList<ClientSession> Sessions
    {
        get
        {
            lock (_lock)
            {
                return _sessions.ToArray();
            }
        }
    }

    public static ClientSession Running(Guid accountId, int processId, bool withWindow = true) => new()
    {
        AccountId = accountId,
        State = ClientSessionState.Running,
        StartedAt = DateTimeOffset.UnixEpoch.AddSeconds(processId),
        ProcessId = processId,
        WindowHandle = withWindow ? FakeWindowService.WindowFor(processId) : 0,
    };

    /// <summary>Adds or replaces the session (removing it when it is no longer alive) and raises SessionChanged.</summary>
    public void Set(ClientSession session)
    {
        lock (_lock)
        {
            _sessions.RemoveAll(existing => existing.AccountId == session.AccountId);
            if (session.IsAlive)
            {
                _sessions.Add(session);
            }
        }

        SessionChanged?.Invoke(this, session);
    }

    public ClientSession Start(Guid accountId, bool withWindow = true)
    {
        int processId;
        lock (_lock)
        {
            processId = ++_nextProcessId;
        }

        var session = Running(accountId, processId, withWindow);
        Set(session);
        return session;
    }

    public ClientSession? Find(Guid accountId)
    {
        lock (_lock)
        {
            return _sessions.Find(session => session.AccountId == accountId);
        }
    }

    public ClientSession? FindByProcessId(int processId)
    {
        lock (_lock)
        {
            return processId == 0 ? null : _sessions.Find(session => session.ProcessId == processId);
        }
    }

    public ClientSession? FindByWindow(nint windowHandle)
    {
        lock (_lock)
        {
            return windowHandle == 0 ? null : _sessions.Find(session => session.WindowHandle == windowHandle);
        }
    }

    public Task<ClientSession> LaunchAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Find(accountId) ?? Start(accountId));

    public Task<IReadOnlyList<ClientSession>> LaunchManyAsync(IReadOnlyList<Guid> accountIds, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _launchManyCalls.Add(accountIds.ToArray());
        }

        IReadOnlyList<ClientSession> sessions = accountIds.Select(id => Find(id) ?? Start(id)).ToArray();
        return Task.FromResult(sessions);
    }

    public bool Stop(Guid accountId)
    {
        var session = Find(accountId);
        if (session is null)
        {
            return false;
        }

        Set(session with { State = ClientSessionState.Exited });
        return true;
    }

    public void StopAll()
    {
        foreach (var session in Sessions)
        {
            Stop(session.AccountId);
        }
    }
}
