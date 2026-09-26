using Microsoft.Extensions.Logging;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Sessions;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Teams;

namespace MultiWiz.Core.Switching;

/// <summary>
/// Keeps the slot order of switchable clients (active team first, then other clients by account order, then clients
/// started outside MultiWiz by label), follows
/// focus changes made anywhere (so <see cref="Current"/> stays right after alt-tab or a click), and applies the
/// audio and performance policies through <see cref="FocusEffects"/>.
/// </summary>
public sealed class ClientSwitcher : IClientSwitcher, IDisposable
{
    private readonly ISessionManager _sessions;
    private readonly ITeamStore _teams;
    private readonly IAccountStore _accounts;
    private readonly ISettingsStore _settings;
    private readonly IWindowService _windows;
    private readonly IWindowEvents _windowEvents;
    private readonly ILogger<ClientSwitcher> _logger;
    private readonly FocusEffects _effects;

    // _lock guards the fields below and is only ever held briefly, because the focus paths (hotkeys on the platform
    // message thread, foreground changes) take it. _rebuildLock serializes Rebuild, which reads the session manager and
    // the stores (possibly waiting on their disk I/O) outside _lock.
    private readonly Lock _lock = new();
    private readonly Lock _rebuildLock = new();

    private ClientSession[] _ordered;
    private Guid? _activeTeamId;
    private Guid? _currentAccountId;
    private AudioSettings _audioSettings;
    private PerformanceSettings _performanceSettings;
    private bool _disposed;

    public ClientSwitcher(
        ISessionManager sessions,
        ITeamStore teams,
        IAccountStore accounts,
        ISettingsStore settings,
        IWindowService windows,
        IWindowEvents windowEvents,
        IAudioService audio,
        IProcessThrottler throttler,
        TimeProvider timeProvider,
        ILogger<ClientSwitcher> logger)
    {
        _sessions = sessions;
        _teams = teams;
        _accounts = accounts;
        _settings = settings;
        _windows = windows;
        _windowEvents = windowEvents;
        _logger = logger;
        _effects = new FocusEffects(audio, throttler, settings, timeProvider, logger, CaptureFocusSnapshot);

        var current = settings.Current;
        _audioSettings = current.Audio;
        _performanceSettings = current.Performance;

        // The last active team keeps its slot order across restarts.
        var lastTeam = current.LastTeamId is { } lastTeamId ? teams.Find(lastTeamId) : null;
        _activeTeamId = lastTeam?.Id;
        _ordered = BuildOrder(lastTeam);

        _sessions.SessionChanged += OnSessionChanged;
        _teams.Changed += OnTeamsOrAccountsChanged;
        _accounts.Changed += OnTeamsOrAccountsChanged;
        _settings.Changed += OnSettingsChanged;
        _windowEvents.ForegroundChanged += OnForegroundChanged;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<ClientSession> OrderedSessions
    {
        get
        {
            lock (_lock)
            {
                return _ordered;
            }
        }
    }

    public ClientSession? Current
    {
        get
        {
            lock (_lock)
            {
                return CurrentLocked();
            }
        }
    }

    public Guid? ActiveTeamId
    {
        get
        {
            lock (_lock)
            {
                return _activeTeamId;
            }
        }
    }

    /// <summary>Also remembers the choice in <see cref="AppSettings.LastTeamId"/> so it survives a restart.</summary>
    public void SetActiveTeam(Guid? teamId)
    {
        lock (_lock)
        {
            if (_activeTeamId == teamId)
            {
                return;
            }

            _activeTeamId = teamId;
        }

        Rebuild(forceChanged: true);

        try
        {
            _settings.Update(settings => settings.LastTeamId == teamId ? settings : settings with { LastTeamId = teamId });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Saving the active team failed");
        }
    }

    public bool FocusSlot(int slotIndex)
    {
        ClientSession? target;
        lock (_lock)
        {
            target = slotIndex >= 0 && slotIndex < _ordered.Length ? _ordered[slotIndex] : null;
        }

        return target is not null && FocusSession(target);
    }

    public bool FocusNext() => FocusRelative(+1);

    public bool FocusPrevious() => FocusRelative(-1);

    public bool Focus(Guid accountId)
    {
        ClientSession? target;
        lock (_lock)
        {
            target = Array.Find(_ordered, session => session.AccountId == accountId);
        }

        return target is not null && FocusSession(target);
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _sessions.SessionChanged -= OnSessionChanged;
        _teams.Changed -= OnTeamsOrAccountsChanged;
        _accounts.Changed -= OnTeamsOrAccountsChanged;
        _settings.Changed -= OnSettingsChanged;
        _windowEvents.ForegroundChanged -= OnForegroundChanged;
        _effects.Dispose();
    }

    private bool FocusRelative(int step)
    {
        ClientSession target;
        lock (_lock)
        {
            if (_ordered.Length == 0)
            {
                return false;
            }

            var index = _currentAccountId is { } currentId ? Array.FindIndex(_ordered, session => session.AccountId == currentId) : -1;
            var next = index < 0
                ? (step > 0 ? 0 : _ordered.Length - 1)
                : ((index + step) % _ordered.Length + _ordered.Length) % _ordered.Length;
            target = _ordered[next];
        }

        return FocusSession(target);
    }

    // Runs synchronously on the caller's thread: hotkey callbacks arrive on the platform message thread, which holds
    // the foreground-activation rights that SetForegroundWindow needs.
    private bool FocusSession(ClientSession session)
    {
        if (!_windows.Focus(session.WindowHandle))
        {
            _logger.LogDebug("Could not focus the window of account {AccountId}", session.AccountId);
            return false;
        }

        SetCurrent(session.AccountId);
        return true;
    }

    private void SetCurrent(Guid accountId)
    {
        lock (_lock)
        {
            if (_currentAccountId == accountId)
            {
                return;
            }

            _currentAccountId = accountId;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        _effects.Schedule();
    }

    // Every change to the inputs (sessions, teams, accounts, active team) is followed by a Rebuild, and rebuilds run one
    // at a time, so the last one always reflects the latest state.
    private void Rebuild(bool forceChanged = false)
    {
        bool changed;
        lock (_rebuildLock)
        {
            Guid? teamId;
            Guid? currentSnapshot;
            lock (_lock)
            {
                teamId = _activeTeamId;
                currentSnapshot = _currentAccountId;
            }

            var team = teamId is { } id ? _teams.Find(id) : null;
            var next = BuildOrder(team);

            // A client whose window took the foreground before the session manager recorded that window is current
            // but not in the order yet; it stays current (CurrentLocked returns null until its window joins the order),
            // so it is focused as soon as the window is detected. Only a client that is gone is forgotten.
            var currentAlive = currentSnapshot is { } snapshotId && _sessions.Find(snapshotId) is { IsAlive: true };

            lock (_lock)
            {
                if (teamId is not null && team is null && _activeTeamId == teamId)
                {
                    // The active team was deleted.
                    _activeTeamId = null;
                    forceChanged = true;
                }

                // If _currentAccountId changed since the snapshot, SetCurrent just set it to an alive session: keep it.
                if (_currentAccountId is { } currentId && currentId == currentSnapshot && !currentAlive)
                {
                    // The focused client exited.
                    _currentAccountId = null;
                    forceChanged = true;
                }

                changed = forceChanged || !Enumerable.SequenceEqual(next, _ordered);
                _ordered = next;
            }
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private ClientSession[] BuildOrder(Team? team)
    {
        var available = new Dictionary<Guid, ClientSession>();
        foreach (var session in _sessions.Sessions)
        {
            if (session.IsAlive && session.HasWindow)
            {
                available[session.AccountId] = session;
            }
        }

        if (available.Count == 0)
        {
            return [];
        }

        var ordered = new List<ClientSession>(available.Count);
        if (team is not null)
        {
            foreach (var accountId in team.AccountIds)
            {
                if (available.Remove(accountId, out var session))
                {
                    ordered.Add(session);
                }
            }
        }

        if (available.Count > 0)
        {
            var rank = new Dictionary<Guid, int>();
            var accounts = _accounts.GetAll();
            for (var i = 0; i < accounts.Count; i++)
            {
                rank.TryAdd(accounts[i].Id, i);
            }

            // Clients started outside MultiWiz come last, by label ("Wizard101 client 2" before "... client 10").
            ordered.AddRange(available.Values
                .OrderBy(session => session.IsExternal ? 1 : 0)
                .ThenBy(session => rank.TryGetValue(session.AccountId, out var position) ? position : int.MaxValue)
                .ThenBy(session => session.Label, ExternalLabelComparer.Instance)
                .ThenBy(session => session.StartedAt));
        }

        return ordered.ToArray();
    }

    private ClientSession? CurrentLocked() =>
        _currentAccountId is { } id ? Array.Find(_ordered, session => session.AccountId == id) : null;

    private FocusSnapshot CaptureFocusSnapshot()
    {
        int? focusedProcessId;
        lock (_lock)
        {
            focusedProcessId = CurrentLocked()?.ProcessId;
        }

        var alive = _sessions.Sessions.Where(session => session.IsAlive && session.ProcessId > 0).ToArray();
        var processIds = alive.Select(session => session.ProcessId).Distinct().ToArray();
        var runningProcessIds = alive
            .Where(session => session.State == ClientSessionState.Running)
            .Select(session => session.ProcessId)
            .Distinct()
            .ToArray();
        return new FocusSnapshot(processIds, runningProcessIds, focusedProcessId is > 0 ? focusedProcessId : null);
    }

    private void OnSessionChanged(object? sender, ClientSession session)
    {
        Rebuild();
        _effects.Schedule();
    }

    private void OnTeamsOrAccountsChanged(object? sender, EventArgs e) => Rebuild();

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        bool relevant;
        lock (_lock)
        {
            relevant = settings.Audio != _audioSettings || settings.Performance != _performanceSettings;
            _audioSettings = settings.Audio;
            _performanceSettings = settings.Performance;
        }

        if (relevant)
        {
            _effects.Schedule();
        }
    }

    private void OnForegroundChanged(object? sender, ForegroundChangedEventArgs e)
    {
        var session = _sessions.FindByWindow(e.WindowHandle) ?? _sessions.FindByProcessId(e.ProcessId);
        if (session is not { IsAlive: true })
        {
            // Not a game window: the last game stays current.
            return;
        }

        SetCurrent(session.AccountId);
    }
}

/// <summary>Orders labels like "Wizard101 client 2" by their text, then by their trailing number as a number.</summary>
internal sealed class ExternalLabelComparer : IComparer<string?>
{
    public static ExternalLabelComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return x is null ? (y is null ? 0 : 1) : -1;
        }

        var (xText, xNumber) = Split(x);
        var (yText, yNumber) = Split(y);
        var byText = string.Compare(xText, yText, StringComparison.OrdinalIgnoreCase);
        return byText != 0 ? byText : xNumber.CompareTo(yNumber);
    }

    private static (string Text, long Number) Split(string label)
    {
        var end = label.Length;
        var start = end;
        while (start > 0 && char.IsAsciiDigit(label[start - 1]))
        {
            start--;
        }

        return start < end && end - start <= 18 && long.TryParse(label.AsSpan(start), out var number)
            ? (label[..start], number)
            : (label, -1);
    }
}
