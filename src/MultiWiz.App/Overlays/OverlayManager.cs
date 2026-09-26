using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using MultiWiz.App.Services;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Sessions;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Switching;
using CorePixelRect = MultiWiz.Core.Primitives.PixelRect;

namespace MultiWiz.App.Overlays;

/// <summary>
/// Keeps one <see cref="NameBadgeOverlay"/> per running client with a window while name badges are enabled.
/// Badges carry the client's switcher slot and are hidden while neither a game nor MultiWiz is in front, so they
/// never float over unrelated apps. Badges of background clients also hide where the game window in front covers
/// them, because every badge lives in the topmost band above all game windows. UI thread, except for the Core event
/// handlers, which only schedule work.
/// </summary>
public sealed class OverlayManager : IDisposable
{
    private readonly IClientSwitcher _switcher;
    private readonly IAccountStore _accounts;
    private readonly ISettingsStore _settings;
    private readonly ISessionManager _sessions;
    private readonly IWindowEvents _windowEvents;
    private readonly IWindowService _windowService;
    private readonly IOverlayWindowStyler _styler;
    private readonly ILogger<OverlayManager> _logger;
    private readonly Dictionary<Guid, NameBadge> _badges = new();
    private readonly UiCoalescer _reconcile;
    private bool _gameOrAppInFront = true;
    private nint _foregroundGameWindow;
    private bool _started;
    private bool _disposed;

    public OverlayManager(
        IClientSwitcher switcher,
        IAccountStore accounts,
        ISettingsStore settings,
        ISessionManager sessions,
        IWindowEvents windowEvents,
        IWindowService windowService,
        IOverlayWindowStyler styler,
        ILogger<OverlayManager> logger)
    {
        _switcher = switcher;
        _accounts = accounts;
        _settings = settings;
        _sessions = sessions;
        _windowEvents = windowEvents;
        _windowService = windowService;
        _styler = styler;
        _logger = logger;
        _reconcile = new UiCoalescer(Reconcile);
    }

    /// <summary>Starts following sessions and settings. UI thread.</summary>
    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        _started = true;
        var foreground = _windowService.GetForegroundWindow();
        var foregroundProcessId = _windowService.GetProcessId(foreground);
        _gameOrAppInFront = IsGameOrApp(foregroundProcessId);
        _foregroundGameWindow = IsGameProcess(foregroundProcessId) ? foreground : 0;

        _switcher.Changed += OnSourceChanged;
        _accounts.Changed += OnSourceChanged;
        _settings.Changed += OnSettingsChanged;
        _windowEvents.ForegroundChanged += OnForegroundChanged;
        Reconcile();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_started)
        {
            _switcher.Changed -= OnSourceChanged;
            _accounts.Changed -= OnSourceChanged;
            _settings.Changed -= OnSettingsChanged;
            _windowEvents.ForegroundChanged -= OnForegroundChanged;
        }

        foreach (var accountId in _badges.Keys.ToArray())
        {
            CloseBadge(accountId);
        }
    }

    private void OnSourceChanged(object? sender, EventArgs e) => _reconcile.Request();

    private void OnSettingsChanged(object? sender, AppSettings settings) => _reconcile.Request();

    // Platform message thread: decide quickly, then hop to the UI thread.
    private void OnForegroundChanged(object? sender, ForegroundChangedEventArgs e)
    {
        var inFront = IsGameOrApp(e.ProcessId);
        var gameWindow = IsGameProcess(e.ProcessId) ? e.WindowHandle : 0;
        Dispatcher.UIThread.Post(() => ApplyForeground(inFront, gameWindow));
    }

    private bool IsGameOrApp(int processId) => processId == Environment.ProcessId || IsGameProcess(processId);

    private bool IsGameProcess(int processId) => processId != 0 && _sessions.FindByProcessId(processId) is not null;

    private void ApplyForeground(bool inFront, nint gameWindow)
    {
        if (_disposed)
        {
            return;
        }

        // Occlusion first, so a badge that is about to be covered is not shown for a moment when un-suppressed.
        _foregroundGameWindow = gameWindow;
        UpdateOcclusion();

        if (_gameOrAppInFront == inFront)
        {
            return;
        }

        _gameOrAppInFront = inFront;
        foreach (var badge in _badges.Values)
        {
            badge.Window.SetSuppressed(!inFront);
        }
    }

    /// <summary>Hides the badges of background clients wherever the game window in front covers them.</summary>
    private void UpdateOcclusion()
    {
        if (_disposed)
        {
            return;
        }

        var cover = _foregroundGameWindow != 0 ? _windowService.GetBounds(_foregroundGameWindow) : null;
        foreach (var badge in _badges.Values)
        {
            var window = badge.Window;
            var occluded = cover is { } front
                && window.TargetWindow != _foregroundGameWindow
                && window.OverlayBounds is { } overlay
                && Intersects(overlay, front);
            window.SetOccluded(occluded);
        }
    }

    private static bool Intersects(CorePixelRect a, CorePixelRect b) =>
        a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;

    private void Reconcile()
    {
        if (_disposed)
        {
            return;
        }

        if (!_settings.Current.Overlays.ShowNameBadges)
        {
            foreach (var accountId in _badges.Keys.ToArray())
            {
                CloseBadge(accountId);
            }

            return;
        }

        var sessions = _switcher.OrderedSessions;
        var wanted = new HashSet<Guid>();
        for (var i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            if (!session.IsAlive || !session.HasWindow)
            {
                continue;
            }

            wanted.Add(session.AccountId);
            _badges.TryGetValue(session.AccountId, out var badge);
            if (badge is not null && badge.Window.TargetWindow != session.WindowHandle)
            {
                CloseBadge(session.AccountId);
                badge = null;
            }

            badge ??= CreateBadge(session);
            if (badge is null)
            {
                continue;
            }

            var account = _accounts.Find(session.AccountId);
            badge.ViewModel.Slot = i + 1;
            badge.ViewModel.Name = account?.DisplayName ?? "Unknown account";
            badge.ViewModel.AccentColor = account?.AccentColor;
        }

        foreach (var accountId in _badges.Keys.Where(id => !wanted.Contains(id)).ToArray())
        {
            CloseBadge(accountId);
        }

        UpdateOcclusion();
    }

    private NameBadge? CreateBadge(ClientSession session)
    {
        var viewModel = new NameBadgeViewModel();
        var window = new NameBadgeOverlay { DataContext = viewModel };
        try
        {
            window.SetSuppressed(!_gameOrAppInFront);
            window.TargetClosed += OnBadgeTargetClosed;
            window.TargetChanged += OnBadgeTargetChanged;
            window.Attach(_styler, _windowEvents, session.WindowHandle);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not attach a name badge to the window of account {AccountId}", session.AccountId);
            window.TargetClosed -= OnBadgeTargetClosed;
            window.TargetChanged -= OnBadgeTargetChanged;
            window.Close();
            return null;
        }

        var badge = new NameBadge(window, viewModel);
        _badges[session.AccountId] = badge;
        return badge;
    }

    private void OnBadgeTargetClosed(object? sender, EventArgs e) => _reconcile.Request();

    // UI thread, before the badge moves: re-check which badges the game in front covers.
    private void OnBadgeTargetChanged(object? sender, EventArgs e) => UpdateOcclusion();

    private void CloseBadge(Guid accountId)
    {
        if (_badges.Remove(accountId, out var badge))
        {
            badge.Window.TargetClosed -= OnBadgeTargetClosed;
            badge.Window.TargetChanged -= OnBadgeTargetChanged;
            if (!badge.Window.IsClosed)
            {
                badge.Window.Close();
            }
        }
    }

    private sealed record NameBadge(NameBadgeOverlay Window, NameBadgeViewModel ViewModel);
}
