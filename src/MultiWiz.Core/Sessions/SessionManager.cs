using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Games;
using MultiWiz.Core.Hotkeys;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Security;
using MultiWiz.Core.Settings;

namespace MultiWiz.Core.Sessions;

/// <summary>
/// Launches game clients, types their credentials and tracks them until they exit (docs/ARCHITECTURE.md, "Sessions").
/// Sessions are keyed by account id and only alive sessions are tracked: a session that fails or exits is removed
/// after its final snapshot is published, and a failed session never leaves its client running.
/// Client processes are started at least <see cref="LoginSettings.StaggerSeconds"/> apart, whichever launch call
/// they come from, and Steam is prepared for one launch at a time.
/// </summary>
public sealed class SessionManager : ISessionManager, ISessionEvents
{
    private static readonly TimeSpan WindowPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FieldDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SteamReadyTimeout = TimeSpan.FromMinutes(3);

    private readonly IAccountStore _accounts;
    private readonly IRealmCatalog _realms;
    private readonly IInstallCatalog _installs;
    private readonly ISettingsStore _settings;
    private readonly ISteamSupport _steam;
    private readonly IProcessLauncher _launcher;
    private readonly IWindowService _windows;
    private readonly IInputSender _input;
    private readonly ICredentialVault _vault;
    private readonly IAudioService _audio;
    private readonly IProcessThrottler _throttler;
    private readonly TimeProvider _time;
    private readonly ILogger<SessionManager> _logger;

    private readonly Lock _lock = new();
    private readonly Dictionary<Guid, SessionEntry> _entries = new();

    // Serializes SessionChanged so subscribers see each session's snapshots in order (see PublishIfLatest).
    private readonly Lock _publishLock = new();

    // The three gates are shared by every launch and live as long as the manager. Their wait handles are never
    // requested, so there is nothing to dispose.
    // Login gate: only one client is typed into at a time.
    private readonly SemaphoreSlim _loginGate = new(1, 1);

    // Steam gate: only one launch at a time checks, starts or waits for Steam and writes steam_appid.txt.
    private readonly SemaphoreSlim _steamGate = new(1, 1);

    // Start gate: processes start one at a time, StaggerSeconds apart; it also guards _lastStartTimestamp.
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private long? _lastStartTimestamp;

    private long _sequence;

    public SessionManager(
        IAccountStore accounts,
        IRealmCatalog realms,
        IInstallCatalog installs,
        ISettingsStore settings,
        ISteamSupport steam,
        IProcessLauncher launcher,
        IWindowService windows,
        IInputSender input,
        ICredentialVault vault,
        IAudioService audio,
        IProcessThrottler throttler,
        TimeProvider timeProvider,
        ILogger<SessionManager> logger)
    {
        _accounts = accounts;
        _realms = realms;
        _installs = installs;
        _settings = settings;
        _steam = steam;
        _launcher = launcher;
        _windows = windows;
        _input = input;
        _vault = vault;
        _audio = audio;
        _throttler = throttler;
        _time = timeProvider;
        _logger = logger;
    }

    public event EventHandler<ClientSession>? SessionChanged;

    public event EventHandler<ClientSession>? LoginCompleted;

    public IReadOnlyList<ClientSession> Sessions
    {
        get
        {
            lock (_lock)
            {
                return _entries.Values.OrderBy(entry => entry.Sequence).Select(entry => entry.Snapshot).ToArray();
            }
        }
    }

    public ClientSession? Find(Guid accountId)
    {
        lock (_lock)
        {
            return _entries.TryGetValue(accountId, out var entry) ? entry.Snapshot : null;
        }
    }

    public ClientSession? FindByProcessId(int processId) =>
        processId == 0 ? null : FindFirst(session => session.ProcessId == processId);

    public ClientSession? FindByWindow(nint windowHandle) =>
        windowHandle == 0 ? null : FindFirst(session => session.WindowHandle == windowHandle);

    public async Task<ClientSession> LaunchAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        if (!TryBeginLaunch(accountId, out var entry, out var snapshot))
        {
            return snapshot;
        }

        _logger.LogInformation("Launching account {AccountId}", accountId);
        PublishIfLatest(entry, snapshot);
        return await RunLaunchAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ClientSession>> LaunchManyAsync(IReadOnlyList<Guid> accountIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountIds);

        // Every launch begins right away and in list order: the start gate spaces the process starts StaggerSeconds
        // apart (in the same order), and the login gate types into one client at a time. Accounts that are already
        // running, or fail before starting a client, make nobody wait. WhenAll also waits for cancelled launches to
        // finish cleaning up before it reports the cancellation.
        var launches = accountIds.Select(accountId => LaunchAsync(accountId, cancellationToken)).ToArray();
        return await Task.WhenAll(launches).ConfigureAwait(false);
    }

    public bool Stop(Guid accountId)
    {
        SessionEntry? entry;
        lock (_lock)
        {
            if (!_entries.TryGetValue(accountId, out entry))
            {
                return false;
            }

            entry.StopRequested = true;
        }

        _logger.LogInformation("Stopping account {AccountId}", accountId);
        KillProcess(entry);
        Cancel(GetFlow(entry));
        return true;
    }

    public void StopAll()
    {
        Guid[] accountIds;
        lock (_lock)
        {
            accountIds = _entries.Keys.ToArray();
        }

        foreach (var accountId in accountIds)
        {
            Stop(accountId);
        }
    }

    private async Task<ClientSession> RunLaunchAsync(SessionEntry entry, CancellationToken cancellationToken)
    {
        using var flow = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SetFlow(entry, flow);
        try
        {
            await LaunchStepsAsync(entry, flow.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (flow.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // The caller gave up: don't leave a half-launched client behind.
                Fail(entry, "The launch was cancelled.");
                KillProcess(entry);
                throw new OperationCanceledException(cancellationToken);
            }

            // Stop() was called or the process exited. A stop ends the session as exited; an exit during the launch
            // was already recorded as a failure by the exit watcher, so this does nothing in that case.
            Transition(entry, static session => session with { State = ClientSessionState.Exited });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Launching account {AccountId} failed unexpectedly", entry.AccountId);
            Fail(entry, $"The launch failed: {ex.Message}");
            KillProcess(entry);
        }
        finally
        {
            SetFlow(entry, null);
        }

        return GetSnapshot(entry);
    }

    private async Task LaunchStepsAsync(SessionEntry entry, CancellationToken token)
    {
        // 1. Account, realm and install.
        var account = _accounts.Find(entry.AccountId);
        if (account is null)
        {
            Fail(entry, "This account no longer exists.");
            return;
        }

        var login = _settings.Current.Login;
        var realm = ResolveRealm(account);
        var install = _installs.Resolve(account);
        if (install is null || !File.Exists(install.ExecutablePath))
        {
            Fail(entry, $"{account.Game} was not found. Set the game folder in Settings → Games.");
            return;
        }

        // Auto-login cannot work without a password, so don't start a client that would only be closed again.
        if (login.AutoLogin && string.IsNullOrEmpty(_vault.GetPassword(account.Id)))
        {
            Fail(entry, "No saved password for this account. Edit the account to save it, or turn off auto-login.");
            return;
        }

        // 2. Steam installs need Steam running and signed in, and Bin\steam_appid.txt in place.
        if (!string.IsNullOrWhiteSpace(install.SteamAppId))
        {
            SteamReadiness steam;
            await _steamGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                steam = await _steam.EnsureReadyAsync(install, SteamReadyTimeout, token).ConfigureAwait(false);
            }
            finally
            {
                _steamGate.Release();
            }

            if (!steam.Ready)
            {
                Fail(entry, string.IsNullOrWhiteSpace(steam.Error) ? "Steam is not ready. Start Steam, sign in, and try again." : steam.Error);
                return;
            }
        }

        // 3. Start the client.
        var process = await StartClientAsync(entry, account, realm, install, token).ConfigureAwait(false);
        if (process is null)
        {
            return;
        }

        var processId = process.Id;
        var stopRequested = AttachProcess(entry, process, processId);
        _ = WatchForExitAsync(entry, process, processId);
        if (stopRequested)
        {
            // Stop() ran while the process was starting and had nothing to kill yet.
            KillProcess(entry);
        }

        // 4. Wait for the main window. A process exit cancels the token, which ends the wait early.
        var timeout = TimeSpan.FromSeconds(Math.Max(1, login.WindowTimeoutSeconds));
        var window = await WaitForWindowAsync(processId, timeout, token).ConfigureAwait(false);
        if (window == 0)
        {
            Fail(entry, "The game window never appeared.");
            KillProcess(entry);
            return;
        }

        // 5. Type the credentials, one client at a time.
        var typedCredentials = false;
        if (login.AutoLogin)
        {
            if (!Transition(entry, session => session with { State = ClientSessionState.WaitingForReady, WindowHandle = window }))
            {
                return;
            }

            var readyDelay = TimeSpan.FromSeconds(Math.Max(0, login.ReadyDelaySeconds));
            if (readyDelay > TimeSpan.Zero)
            {
                await Task.Delay(readyDelay, _time, token).ConfigureAwait(false);
            }

            await _loginGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!Transition(entry, static session => session with { State = ClientSessionState.LoggingIn }))
                {
                    return;
                }

                // Checked again: the password can be deleted while the client loads.
                var password = _vault.GetPassword(account.Id);
                if (string.IsNullOrEmpty(password))
                {
                    Fail(entry, "No saved password for this account. Edit the account to save it, or turn off auto-login.");
                    KillProcess(entry);
                    return;
                }

                try
                {
                    await TypeCredentialsAsync(window, account.Username, password, login.KeystrokeDelayMs, token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Typing the login details for account {AccountId} failed", account.Id);
                    Fail(entry, $"Typing the login details failed: {ex.Message}");
                    KillProcess(entry);
                    return;
                }

                typedCredentials = true;
            }
            finally
            {
                _loginGate.Release();
            }
        }

        // 6. Running.
        if (!Transition(entry, session => session with { State = ClientSessionState.Running, WindowHandle = window }))
        {
            return;
        }

        if (typedCredentials && login.RefocusAfterLogin)
        {
            RaiseLoginCompleted(GetSnapshot(entry));
        }
    }

    /// <summary>
    /// Starts the client once it is this launch's turn and at least <see cref="LoginSettings.StaggerSeconds"/> have passed
    /// since the previous start. Returns null, with the session failed, if the process could not be started.
    /// </summary>
    private async Task<ILaunchedProcess?> StartClientAsync(SessionEntry entry, Account account, Realm realm, GameInstall install, CancellationToken token)
    {
        await _startGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_lastStartTimestamp is { } lastStart)
            {
                var stagger = TimeSpan.FromSeconds(Math.Max(0, _settings.Current.Login.StaggerSeconds));
                var wait = stagger - _time.GetElapsedTime(lastStart);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, _time, token).ConfigureAwait(false);
                }
            }

            token.ThrowIfCancellationRequested();
            try
            {
                var process = _launcher.Start(new ProcessLaunchRequest(install.ExecutablePath, install.BinPath, LaunchArguments.Build(realm, install)));
                _lastStartTimestamp = _time.GetTimestamp();
                return process;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Starting {Executable} failed", install.ExecutablePath);
                Fail(entry, $"{account.Game} could not be started: {ex.Message}");
                return null;
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task<nint> WaitForWindowAsync(int processId, TimeSpan timeout, CancellationToken token)
    {
        var started = _time.GetTimestamp();
        while (true)
        {
            var window = _windows.FindMainWindow(processId);
            if (window != 0)
            {
                return window;
            }

            if (_time.GetElapsedTime(started) >= timeout)
            {
                return 0;
            }

            await Task.Delay(WindowPollInterval, _time, token).ConfigureAwait(false);
        }
    }

    private async Task TypeCredentialsAsync(nint window, string username, string password, int keystrokeDelayMs, CancellationToken token)
    {
        var perCharacter = TimeSpan.FromMilliseconds(Math.Max(0, keystrokeDelayMs));
        await _input.SendTextAsync(window, username, perCharacter, token).ConfigureAwait(false);
        await Task.Delay(FieldDelay, _time, token).ConfigureAwait(false);
        await _input.SendKeyAsync(window, VirtualKeys.Tab, token).ConfigureAwait(false);
        await Task.Delay(FieldDelay, _time, token).ConfigureAwait(false);
        await _input.SendTextAsync(window, password, perCharacter, token).ConfigureAwait(false);
        await Task.Delay(FieldDelay, _time, token).ConfigureAwait(false);
        await _input.SendKeyAsync(window, VirtualKeys.Enter, token).ConfigureAwait(false);
    }

    // 7. Runs for the lifetime of the process, independent of the launch flow.
    private async Task WatchForExitAsync(SessionEntry entry, ILaunchedProcess process, int processId)
    {
        try
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Waiting for process {ProcessId} to exit failed; treating it as exited", processId);
        }

        OnProcessExited(entry, process, processId);
    }

    private void OnProcessExited(SessionEntry entry, ILaunchedProcess process, int processId)
    {
        ClientSession? updated = null;
        CancellationTokenSource? flow;
        lock (_lock)
        {
            entry.ProcessExited = true;
            flow = entry.Flow;
            if (entry.Snapshot.IsAlive)
            {
                updated = entry.StopRequested || entry.Snapshot.State == ClientSessionState.Running
                    ? entry.Snapshot with { State = ClientSessionState.Exited }
                    : entry.Snapshot with { State = ClientSessionState.Failed, Error = "The game closed before it finished starting." };
                entry.Snapshot = updated;
                RemoveLocked(entry);
            }
        }

        _logger.LogInformation("Process {ProcessId} of account {AccountId} exited", processId, entry.AccountId);
        ReleaseProcessResources(processId);
        if (updated is not null)
        {
            PublishIfLatest(entry, updated);
        }

        // Safe to dispose now: KillProcess skips processes marked as exited, and the launch flow never touches the
        // process object after starting its watcher.
        process.Dispose();

        // Ends a launch that is still waiting for the window or the login.
        Cancel(flow);
    }

    private bool TryBeginLaunch(Guid accountId, [NotNullWhen(true)] out SessionEntry? entry, out ClientSession snapshot)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(accountId, out var existing))
            {
                entry = null;
                snapshot = existing.Snapshot;
                return false;
            }

            snapshot = new ClientSession
            {
                AccountId = accountId,
                State = ClientSessionState.Launching,
                StartedAt = _time.GetUtcNow(),
            };
            entry = new SessionEntry(accountId, ++_sequence, snapshot);
            _entries.Add(accountId, entry);
            return true;
        }
    }

    /// <summary>Records the started process and moves to WaitingForWindow. Returns true if a stop was requested meanwhile.</summary>
    private bool AttachProcess(SessionEntry entry, ILaunchedProcess process, int processId)
    {
        ClientSession? updated = null;
        bool stopRequested;
        lock (_lock)
        {
            entry.Process = process;
            stopRequested = entry.StopRequested;
            if (entry.Snapshot.IsAlive)
            {
                updated = entry.Snapshot with { State = ClientSessionState.WaitingForWindow, ProcessId = processId };
                entry.Snapshot = updated;
            }
        }

        if (updated is not null)
        {
            PublishIfLatest(entry, updated);
        }

        return stopRequested;
    }

    /// <summary>Applies a state change unless the session already ended. Publishes the new snapshot outside the lock.</summary>
    private bool Transition(SessionEntry entry, Func<ClientSession, ClientSession> change)
    {
        ClientSession updated;
        lock (_lock)
        {
            if (!entry.Snapshot.IsAlive)
            {
                return false;
            }

            updated = change(entry.Snapshot);
            entry.Snapshot = updated;
            if (!updated.IsAlive)
            {
                RemoveLocked(entry);
            }
        }

        _logger.LogDebug("Account {AccountId} is now {State}", updated.AccountId, updated.State);
        PublishIfLatest(entry, updated);
        return true;
    }

    private void Fail(SessionEntry entry, string error)
    {
        if (Transition(entry, session => session with { State = ClientSessionState.Failed, Error = error }))
        {
            _logger.LogWarning("Launching account {AccountId} failed: {Error}", entry.AccountId, error);
        }
    }

    private void KillProcess(SessionEntry entry)
    {
        ILaunchedProcess? process;
        lock (_lock)
        {
            process = entry.ProcessExited ? null : entry.Process;
        }

        if (process is null)
        {
            return;
        }

        // Restore the volume while the client's audio session still exists: Windows saves a session's volume per
        // executable, so a client killed while muted would make the next one start muted.
        RestoreVolume(process.Id);

        try
        {
            process.Kill();
        }
        catch (Exception ex)
        {
            // Usually the process exited (and was disposed) at the same moment.
            _logger.LogWarning(ex, "Killing the client of account {AccountId} failed", entry.AccountId);
        }
    }

    private void ReleaseProcessResources(int processId)
    {
        RestoreVolume(processId);

        try
        {
            _throttler.Release(processId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Releasing the throttling of process {ProcessId} failed", processId);
        }
    }

    private void RestoreVolume(int processId)
    {
        try
        {
            _audio.Release(processId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Restoring the volume of process {ProcessId} failed", processId);
        }
    }

    private Realm ResolveRealm(Account account)
    {
        var realm = _realms.Find(account.RealmId);
        if (realm is not null && realm.Game == account.Game)
        {
            return realm;
        }

        var fallback = _realms.DefaultFor(account.Game);
        _logger.LogWarning(
            "Realm {RealmId} of account {AccountId} is unknown or belongs to another game; using {FallbackRealmId}",
            account.RealmId, account.Id, fallback.Id);
        return fallback;
    }

    /// <summary>
    /// Raises <see cref="SessionChanged"/> with <paramref name="snapshot"/> unless a newer snapshot of the session was
    /// recorded meanwhile. Snapshots are recorded under <c>_lock</c> but raised after it is released, so two
    /// threads (say the launch flow and the exit watcher) could otherwise deliver them out of order and leave
    /// subscribers on a stale state. A skipped snapshot loses nothing: its successor is, or will be, raised by the
    /// thread that recorded it.
    /// </summary>
    private void PublishIfLatest(SessionEntry entry, ClientSession snapshot)
    {
        lock (_publishLock)
        {
            bool latest;
            lock (_lock)
            {
                latest = ReferenceEquals(entry.Snapshot, snapshot);
            }

            if (latest)
            {
                Publish(snapshot);
            }
        }
    }

    private void Publish(ClientSession snapshot)
    {
        try
        {
            SessionChanged?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            // A faulty subscriber must not break the launch flow or the exit watcher.
            _logger.LogError(ex, "A SessionChanged handler threw");
        }
    }

    private void RaiseLoginCompleted(ClientSession snapshot)
    {
        try
        {
            LoginCompleted?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A LoginCompleted handler threw");
        }
    }

    private ClientSession? FindFirst(Func<ClientSession, bool> predicate)
    {
        lock (_lock)
        {
            foreach (var entry in _entries.Values)
            {
                if (predicate(entry.Snapshot))
                {
                    return entry.Snapshot;
                }
            }

            return null;
        }
    }

    private ClientSession GetSnapshot(SessionEntry entry)
    {
        lock (_lock)
        {
            return entry.Snapshot;
        }
    }

    private CancellationTokenSource? GetFlow(SessionEntry entry)
    {
        lock (_lock)
        {
            return entry.Flow;
        }
    }

    private void SetFlow(SessionEntry entry, CancellationTokenSource? flow)
    {
        lock (_lock)
        {
            entry.Flow = flow;
        }
    }

    private void RemoveLocked(SessionEntry entry)
    {
        if (_entries.TryGetValue(entry.AccountId, out var current) && ReferenceEquals(current, entry))
        {
            _entries.Remove(entry.AccountId);
        }
    }

    private static void Cancel(CancellationTokenSource? flow)
    {
        if (flow is null)
        {
            return;
        }

        try
        {
            flow.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The launch finished at the same moment; there is nothing left to cancel.
        }
    }

    private sealed class SessionEntry(Guid accountId, long sequence, ClientSession snapshot)
    {
        public Guid AccountId { get; } = accountId;

        public long Sequence { get; } = sequence;

        // Everything below is guarded by SessionManager._lock.
        public ClientSession Snapshot { get; set; } = snapshot;

        public ILaunchedProcess? Process { get; set; }

        public CancellationTokenSource? Flow { get; set; }

        public bool StopRequested { get; set; }

        public bool ProcessExited { get; set; }
    }
}
