using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Settings;
using Velopack;
using Velopack.Sources;

namespace MultiWiz.App.Services;

public enum UpdateState
{
    /// <summary>Not a Velopack install (development build); updates are unavailable.</summary>
    Unavailable,
    Idle,
    Checking,
    UpToDate,
    Downloading,
    ReadyToRestart,
    Failed,
}

/// <summary>
/// Velopack updates from GitHub releases. Stable uses the default "win" channel (so MultiWiz 3 installs update into
/// v4); Beta checks the "beta" feed and the stable feed and offers whichever is newer. Checks on startup (when enabled)
/// and every 6 hours, downloads in the background, and applies when the user chooses to restart; a downloaded update
/// the user never restarted for is applied by VelopackApp the next time MultiWiz starts while no other instance runs
/// (see Program.Main). Observable properties change on the UI thread.
/// </summary>
public sealed partial class UpdateService : ObservableObject, IDisposable
{
    private const string StableChannel = "win";
    private const string BetaChannel = "beta";
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly ISettingsStore _settings;
    private readonly WindowCoordinator _windows;
    private readonly ILogger<UpdateService> _logger;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly Lock _pendingLock = new();
    private readonly UpdateManager? _stableManager;
    private UpdateManager? _betaManager;
    private UpdateManager? _pendingManager;
    private VelopackAsset? _pendingAsset;
    private bool _started;
    private bool _disposed;

    public UpdateService(ISettingsStore settings, WindowCoordinator windows, ILogger<UpdateService> logger)
    {
        _settings = settings;
        _windows = windows;
        _logger = logger;

        try
        {
            // A prerelease build may move back to a lower stable version when the user switches to the stable channel.
            _stableManager = CreateManager(prerelease: false, StableChannel, allowDowngrade: AppInfo.Version.Contains('-'));
            IsInstalled = _stableManager.IsInstalled;
            CurrentVersion = _stableManager.CurrentVersion?.ToFullString() ?? AppInfo.Version;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Velopack is not available; updates are disabled");
            _stableManager = null;
            IsInstalled = false;
            CurrentVersion = AppInfo.Version;
        }

        State = IsInstalled ? UpdateState.Idle : UpdateState.Unavailable;
        StatusText = IsInstalled ? string.Empty : "Updates are available in installed builds only.";
    }

    public bool IsInstalled { get; }

    public string CurrentVersion { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUpdateReady))]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    public partial UpdateState State { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int DownloadProgress { get; private set; }

    [ObservableProperty]
    public partial string? AvailableVersion { get; private set; }

    public bool IsUpdateReady => State == UpdateState.ReadyToRestart;

    public bool IsBusy => State is UpdateState.Checking or UpdateState.Downloading;

    /// <summary>Starts the startup and periodic checks. UI thread.</summary>
    public void Start()
    {
        if (_started || !IsInstalled || _stableManager is null)
        {
            return;
        }

        _started = true;
        if (_stableManager.UpdatePendingRestart is { } pending)
        {
            SetPending(_stableManager, pending);
            SetStatus(UpdateState.ReadyToRestart, $"Update {pending.Version.ToFullString()} is ready. Restart to apply.");
        }

        _ = RunScheduleAsync(_lifetime.Token);
    }

    /// <summary>Checks now (the "Check now" button).</summary>
    public Task CheckNowAsync() => CheckAsync(userInitiated: true, _lifetime.Token);

    /// <summary>Applies the downloaded update after MultiWiz exits, then restarts it.</summary>
    public void RestartToApply()
    {
        UpdateManager? manager;
        VelopackAsset? asset;
        lock (_pendingLock)
        {
            manager = _pendingManager;
            asset = _pendingAsset;
        }

        if (manager is null || asset is null)
        {
            return;
        }

        try
        {
            manager.WaitExitThenApplyUpdates(asset, silent: false, restart: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start applying update {Version}", asset.Version.ToFullString());
            SetStatus(UpdateState.Failed, "The update could not be applied. Try again later.");
            return;
        }

        _logger.LogInformation("Restarting to apply update {Version}", asset.Version.ToFullString());
        _windows.Quit();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private async Task RunScheduleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(StartupDelay, cancellationToken).ConfigureAwait(false);
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_settings.Current.General.CheckForUpdates)
                {
                    await CheckAsync(userInitiated: false, cancellationToken).ConfigureAwait(false);
                }

                await Task.Delay(CheckInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task CheckAsync(bool userInitiated, CancellationToken cancellationToken)
    {
        if (!IsInstalled || _stableManager is null)
        {
            return;
        }

        if (!await _checkGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return; // A check or download is already running.
        }

        try
        {
            if (HasPendingUpdate())
            {
                return;
            }

            SetStatus(UpdateState.Checking, "Checking for updates…");
            var channel = _settings.Current.General.UpdateChannel;
            var candidate = await FindUpdateAsync(_stableManager, channel, userInitiated, cancellationToken).ConfigureAwait(false);
            if (candidate is not { } update)
            {
                _logger.LogInformation("No update available on the {Channel} channel", channel);
                SetStatus(UpdateState.UpToDate, $"You're up to date ({CurrentVersion}).");
                return;
            }

            var version = update.Info.TargetFullRelease.Version.ToFullString();
            var isDowngrade = update.Info.IsDowngrade;
            _logger.LogInformation(
                "Downloading update {Version} ({Channel} channel, downgrade: {IsDowngrade})", version, channel, isDowngrade);
            Dispatcher.UIThread.Post(() =>
            {
                AvailableVersion = version;
                DownloadProgress = 0;
            });
            SetStatus(UpdateState.Downloading, $"Downloading MultiWiz {version}…");

            await update.Manager.DownloadUpdatesAsync(
                    update.Info,
                    progress => Dispatcher.UIThread.Post(() => DownloadProgress = progress),
                    cancellationToken)
                .ConfigureAwait(false);

            SetPending(update.Manager, update.Info.TargetFullRelease);
            _logger.LogInformation("Update {Version} downloaded and ready", version);
            SetStatus(
                UpdateState.ReadyToRestart,
                isDowngrade
                    ? $"MultiWiz {version} (stable) is ready. Restart to switch back to the stable channel."
                    : $"MultiWiz {version} is ready. Restart to apply.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
            SetStatus(
                UpdateState.Failed,
                userInitiated ? "Couldn't reach GitHub to check for updates. Try again later." : "The last update check failed.");
        }
        finally
        {
            _checkGate.Release();
        }
    }

    private async Task<(UpdateManager Manager, UpdateInfo Info)?> FindUpdateAsync(
        UpdateManager stableManager, UpdateChannel channel, bool userInitiated, CancellationToken cancellationToken)
    {
        var stableUpdate = await stableManager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (channel == UpdateChannel.Stable)
        {
            // Never move back to an older major version: until a stable 4.x exists, the stable feed's newest release
            // is MultiWiz 3, and a v4 beta must not be "updated" to it.
            if (stableUpdate is { IsDowngrade: true }
                && (stableManager.CurrentVersion is not { } current || stableUpdate.TargetFullRelease.Version.Major < current.Major))
            {
                _logger.LogInformation(
                    "Stable release {Version} belongs to an older major version than this build; not offering it",
                    stableUpdate.TargetFullRelease.Version.ToFullString());
                return null;
            }

            // A beta build moves back to an older stable release only when the user asks for it with "Check now";
            // otherwise a beta install (which starts on the Stable setting) would be downgraded in the background.
            if (stableUpdate is { IsDowngrade: true } && !userInitiated)
            {
                _logger.LogInformation(
                    "Stable release {Version} is older than this build; waiting for the user to check manually",
                    stableUpdate.TargetFullRelease.Version.ToFullString());
                return null;
            }

            return stableUpdate is null ? null : (stableManager, stableUpdate);
        }

        _betaManager ??= CreateManager(prerelease: true, BetaChannel, allowDowngrade: false);
        var betaUpdate = await _betaManager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);

        // On the beta channel only ever move forward, taking whichever feed has the higher version.
        if (stableUpdate is { IsDowngrade: true })
        {
            stableUpdate = null;
        }

        if (betaUpdate is { IsDowngrade: true })
        {
            betaUpdate = null;
        }

        if (betaUpdate is null)
        {
            return stableUpdate is null ? null : (stableManager, stableUpdate);
        }

        if (stableUpdate is null)
        {
            return (_betaManager, betaUpdate);
        }

        return stableUpdate.TargetFullRelease.Version > betaUpdate.TargetFullRelease.Version
            ? (stableManager, stableUpdate)
            : (_betaManager, betaUpdate);
    }

    private static UpdateManager CreateManager(bool prerelease, string channel, bool allowDowngrade) =>
        new(
            new GithubSource(AppInfo.RepositoryUrl, null, prerelease),
            new UpdateOptions { ExplicitChannel = channel, AllowVersionDowngrade = allowDowngrade });

    private bool HasPendingUpdate()
    {
        lock (_pendingLock)
        {
            return _pendingAsset is not null;
        }
    }

    private void SetPending(UpdateManager manager, VelopackAsset asset)
    {
        lock (_pendingLock)
        {
            _pendingManager = manager;
            _pendingAsset = asset;
        }
    }

    private void SetStatus(UpdateState state, string text)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            State = state;
            StatusText = text;
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                State = state;
                StatusText = text;
            });
        }
    }
}
