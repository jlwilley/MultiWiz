using Microsoft.Extensions.Logging;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Platform;
using MultiWiz.Core.Settings;

namespace MultiWiz.Core.Games;

/// <summary>
/// Discovered installs (from <see cref="IInstallLocator"/>, cached until <see cref="Refresh"/>) plus the custom
/// installs from <see cref="AppSettings.CustomInstalls"/>. Lists are ordered Standalone, Steam, Custom.
/// </summary>
public sealed class InstallCatalog : IInstallCatalog, IDisposable
{
    private readonly IInstallLocator _locator;
    private readonly ISettingsStore _settings;
    private readonly ILogger<InstallCatalog> _logger;
    private readonly Lock _lock = new();
    private IReadOnlyList<GameInstall>? _discovered;
    private IReadOnlyList<GameInstall> _customInstalls;

    public InstallCatalog(IInstallLocator locator, ISettingsStore settings, ILogger<InstallCatalog> logger)
    {
        _locator = locator;
        _settings = settings;
        _logger = logger;
        _customInstalls = settings.Current.CustomInstalls;
        _settings.Changed += OnSettingsChanged;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<GameInstall> GetAll()
    {
        var discovered = GetDiscovered();
        var custom = _settings.Current.CustomInstalls;
        var all = new List<GameInstall>(discovered.Count + custom.Count);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var install in discovered.Concat(custom))
        {
            if (!string.IsNullOrWhiteSpace(install.Id) && ids.Add(install.Id))
            {
                all.Add(install);
            }
        }

        // OrderBy is stable, so discovery order is kept within each source.
        return all.OrderBy(install => install.Source).ToArray();
    }

    public GameInstall? Find(string installId) => FindIn(GetAll(), installId);

    public IReadOnlyList<GameInstall> ForGame(GameKind game) => GetAll().Where(install => install.Game == game).ToArray();

    public GameInstall? Resolve(Account account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var candidates = ForGame(account.Game);
        if (candidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(account.InstallId) && FindIn(candidates, account.InstallId) is { } own)
        {
            return own;
        }

        if (_settings.Current.PreferredInstallIds.TryGetValue(account.Game, out var preferredId) &&
            FindIn(candidates, preferredId) is { } preferred)
        {
            return preferred;
        }

        return candidates[0];
    }

    public void Refresh()
    {
        lock (_lock)
        {
            _discovered = Discover();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _settings.Changed -= OnSettingsChanged;

    private IReadOnlyList<GameInstall> GetDiscovered()
    {
        lock (_lock)
        {
            return _discovered ??= Discover();
        }
    }

    private IReadOnlyList<GameInstall> Discover()
    {
        try
        {
            var found = _locator.Discover().ToArray();
            _logger.LogInformation("Found {Count} game installs", found.Length);
            return found;
        }
        catch (Exception ex)
        {
            // Discovery reads the registry and Steam files; a failure there must not take the launcher down.
            _logger.LogError(ex, "Discovering game installs failed");
            return [];
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        var previous = Interlocked.Exchange(ref _customInstalls, settings.CustomInstalls);
        if (!ReferenceEquals(previous, settings.CustomInstalls))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static GameInstall? FindIn(IReadOnlyList<GameInstall> installs, string? installId) =>
        installs.FirstOrDefault(install => string.Equals(install.Id, installId, StringComparison.OrdinalIgnoreCase));
}
