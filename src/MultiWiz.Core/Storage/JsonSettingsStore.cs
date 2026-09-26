using Microsoft.Extensions.Logging;
using MultiWiz.Core.Games;
using MultiWiz.Core.Hotkeys;
using MultiWiz.Core.Settings;

namespace MultiWiz.Core.Storage;

/// <summary>
/// <see cref="ISettingsStore"/> backed by <see cref="AppPaths.SettingsFile"/>. Loads on first use, clamps obviously
/// invalid values (volumes 0..100, opacity 0.2..1, delays &gt;= 0), and saves on every change.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    public const double MinimumSwitcherOpacity = 0.2;

    private readonly string _path;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JsonSettingsStore> _logger;
    private readonly Lock _lock = new();
    private AppSettings? _current;
    private string? _recoveredFromCorruptFile;

    public JsonSettingsStore(AppPaths paths, TimeProvider timeProvider, ILogger<JsonSettingsStore> logger)
    {
        _path = paths.SettingsFile;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public event EventHandler<AppSettings>? Changed;

    /// <summary>
    /// Where the settings file was moved because it could not be read (it was not valid JSON), or null. The store then
    /// started from the default settings, so the UI can tell the user where the old file is. Reading this loads the
    /// store if it has not been used yet.
    /// </summary>
    public string? RecoveredFromCorruptFile
    {
        get
        {
            lock (_lock)
            {
                EnsureLoaded();
                return _recoveredFromCorruptFile;
            }
        }
    }

    public AppSettings Current
    {
        get
        {
            lock (_lock)
            {
                return EnsureLoaded();
            }
        }
    }

    public void Update(Func<AppSettings, AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        AppSettings next;
        lock (_lock)
        {
            var current = EnsureLoaded();
            next = Sanitize(mutate(current) ?? throw new InvalidOperationException("A settings update returned null."));
            if (next == current)
            {
                return;
            }

            JsonFileStore.Save(_path, next, CoreJsonContext.Default.AppSettings);
            _current = next;
        }

        Changed?.Invoke(this, next);
    }

    private AppSettings EnsureLoaded()
    {
        if (_current is not null)
        {
            return _current;
        }

        var loaded = JsonFileStore.Load(
            _path,
            CoreJsonContext.Default.AppSettings,
            _logger,
            _timeProvider,
            CreateFileDefaults(),
            target => _recoveredFromCorruptFile = target);
        _current = Sanitize(loaded ?? new AppSettings());
        return _current;
    }

    // Settings the file lacks (written by an older version, or edited by hand) keep their C# defaults.
    private static JsonDefaults CreateFileDefaults() =>
        JsonDefaults.From(new AppSettings(), CoreJsonContext.Default.AppSettings)
            .WithArrayElements(
                nameof(AppSettings.CustomRealms),
                JsonDefaults.From(
                    new Realm { Id = string.Empty, Game = GameKind.Wizard101, DisplayName = string.Empty, LoginHost = string.Empty },
                    CoreJsonContext.Default.Realm));

    /// <summary>Upgrades settings written by older builds.</summary>
    private static AppSettings Migrate(AppSettings settings)
    {
        if (settings.SchemaVersion < 3)
        {
            // Schema 2 raised the login delay default from 4 s to 8 s while the login was being typed into the wrong
            // window. With that fixed, 4 s is enough again, so the automatic 8 s goes back to 4 s. Deliberate values stay.
            var login = settings.Login ?? new LoginSettings();
            var automatic = settings.SchemaVersion == 2 && login.ReadyDelaySeconds == 8;
            settings = settings with
            {
                SchemaVersion = 3,
                Login = automatic ? login with { ReadyDelaySeconds = new LoginSettings().ReadyDelaySeconds } : login,
            };
        }

        return settings;
    }

    /// <summary>
    /// Replaces missing sections with defaults, drops unusable custom installs and realms, and clamps out-of-range
    /// values. Returns a value-equal copy when nothing needed fixing, so <c>==</c> still detects "no change".
    /// </summary>
    private static AppSettings Sanitize(AppSettings settings)
    {
        settings = Migrate(settings);
        var general = settings.General ?? new GeneralSettings();
        var login = settings.Login ?? new LoginSettings();
        var audio = settings.Audio ?? new AudioSettings();
        var switcher = settings.Switcher ?? new SwitcherSettings();
        var hotkeys = settings.Hotkeys ?? new HotkeySettings();

        return settings with
        {
            General = general with
            {
                Theme = Enum.IsDefined(general.Theme) ? general.Theme : ThemePreference.Dark,
                UpdateChannel = Enum.IsDefined(general.UpdateChannel) ? general.UpdateChannel : UpdateChannel.Stable,
            },
            Login = login with
            {
                ReadyDelaySeconds = Math.Max(0, login.ReadyDelaySeconds),
                WindowTimeoutSeconds = Math.Max(1, login.WindowTimeoutSeconds),
                KeystrokeDelayMs = Math.Max(0, login.KeystrokeDelayMs),
                StaggerSeconds = Math.Max(0, login.StaggerSeconds),
            },
            Audio = audio with
            {
                FocusedVolumePercent = Math.Clamp(audio.FocusedVolumePercent, 0, 100),
                UnfocusedVolumePercent = Math.Clamp(audio.UnfocusedVolumePercent, 0, 100),
            },
            Switcher = switcher with
            {
                Opacity = double.IsFinite(switcher.Opacity)
                    ? Math.Clamp(switcher.Opacity, MinimumSwitcherOpacity, 1.0)
                    : new SwitcherSettings().Opacity,
            },
            Overlays = settings.Overlays ?? new OverlaySettings(),
            Performance = settings.Performance ?? new PerformanceSettings(),
            Hotkeys = hotkeys.Bindings is null ? hotkeys with { Bindings = new Dictionary<HotkeyAction, string>() } : hotkeys,
            // A game or install source this build does not know (written by a newer build) reads as undefined; such
            // entries cannot be used here, so they are dropped instead of failing the whole file.
            CustomInstalls = KeepValid(
                settings.CustomInstalls,
                install => install is not null
                    && !string.IsNullOrWhiteSpace(install.Id)
                    && !string.IsNullOrWhiteSpace(install.RootPath)
                    && Enum.IsDefined(install.Game)
                    && Enum.IsDefined(install.Source)),
            CustomRealms = KeepValid(settings.CustomRealms, realm => realm is not null && Enum.IsDefined(realm.Game)),
            PreferredInstallIds = settings.PreferredInstallIds ?? new Dictionary<GameKind, string>(),
        };
    }

    // Returns the same list instance when every item is valid, so unchanged settings stay reference-equal.
    private static IReadOnlyList<T> KeepValid<T>(IReadOnlyList<T>? items, Func<T, bool> isValid)
        where T : class
    {
        if (items is null)
        {
            return [];
        }

        return items.All(isValid) ? items : items.Where(isValid).ToArray();
    }
}
