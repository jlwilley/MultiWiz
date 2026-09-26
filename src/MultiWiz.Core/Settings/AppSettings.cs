using System.Text.Json;
using System.Text.Json.Serialization;
using MultiWiz.Core.Games;
using MultiWiz.Core.Hotkeys;
using MultiWiz.Core.Storage;

namespace MultiWiz.Core.Settings;

public enum ThemePreference { System = 0, Dark = 1, Light = 2 }

public enum UpdateChannel { Stable = 0, Beta = 1 }

/// <summary>All user preferences. Immutable; change via <see cref="ISettingsStore.Update"/>.</summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public GeneralSettings General { get; init; } = new();
    public LoginSettings Login { get; init; } = new();
    public AudioSettings Audio { get; init; } = new();
    public SwitcherSettings Switcher { get; init; } = new();
    public OverlaySettings Overlays { get; init; } = new();
    public PerformanceSettings Performance { get; init; } = new();
    public HotkeySettings Hotkeys { get; init; } = new();

    /// <summary>Installs the user added by hand (discovered installs are not persisted).</summary>
    public IReadOnlyList<GameInstall> CustomInstalls { get; init; } = [];

    /// <summary>Preferred install id per game, used when an account's InstallId is null.</summary>
    [JsonConverter(typeof(TolerantEnumKeyDictionaryConverter<GameKind>))]
    public IReadOnlyDictionary<GameKind, string> PreferredInstallIds { get; init; } = new Dictionary<GameKind, string>();

    public IReadOnlyList<Realm> CustomRealms { get; init; } = [];

    /// <summary>True once the v3 config import prompt has been shown (accepted or declined).</summary>
    public bool LegacyImportHandled { get; init; }

    public Guid? LastTeamId { get; init; }

    /// <summary>
    /// Properties this build does not know (written by a newer one), kept so a save does not drop them. Only the
    /// serializer sets this (extension data cannot be init-only with source-generated metadata).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed record GeneralSettings
{
    [JsonConverter(typeof(TolerantEnumConverter<ThemePreference>))]
    public ThemePreference Theme { get; init; } = ThemePreference.Dark;
    public bool MinimizeToTray { get; init; } = true;
    public bool CloseToTray { get; init; }
    public bool CloseGamesOnExit { get; init; }
    public bool CheckForUpdates { get; init; } = true;
    [JsonConverter(typeof(TolerantEnumConverter<UpdateChannel>))]
    public UpdateChannel UpdateChannel { get; init; } = UpdateChannel.Stable;

    /// <summary>
    /// Pick up Wizard101/Pirate101 clients started outside MultiWiz (the official launcher, or before MultiWiz ran), so
    /// they get slots, audio switching and name badges too.
    /// </summary>
    public bool DetectExternalClients { get; init; } = true;

    /// <summary>
    /// Properties this build does not know (written by a newer one), kept so a save does not drop them. Only the
    /// serializer sets this (extension data cannot be init-only with source-generated metadata).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed record LoginSettings
{
    /// <summary>Type the username/password into the client automatically.</summary>
    public bool AutoLogin { get; init; } = true;

    /// <summary>Seconds to wait after the game window appears before typing (the login screen loads after the window).</summary>
    public int ReadyDelaySeconds { get; init; } = 4;

    /// <summary>Give up if no client window appears within this many seconds.</summary>
    public int WindowTimeoutSeconds { get; init; } = 90;

    /// <summary>Delay between typed characters.</summary>
    public int KeystrokeDelayMs { get; init; } = 15;

    /// <summary>Seconds between starting consecutive clients when launching several.</summary>
    public int StaggerSeconds { get; init; } = 2;

    /// <summary>Bring MultiWiz back to the front after a login finishes.</summary>
    public bool RefocusAfterLogin { get; init; }

    /// <summary>
    /// Properties this build does not know (written by a newer one), kept so a save does not drop them. Only the
    /// serializer sets this (extension data cannot be init-only with source-generated metadata).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed record AudioSettings
{
    /// <summary>Adjust client volumes when focus changes.</summary>
    public bool Enabled { get; init; } = true;
    public int FocusedVolumePercent { get; init; } = 100;

    /// <summary>0 mutes background clients; anything higher ducks them.</summary>
    public int UnfocusedVolumePercent { get; init; }

    /// <summary>
    /// Properties this build does not know (written by a newer one), kept so a save does not drop them. Only the
    /// serializer sets this (extension data cannot be init-only with source-generated metadata).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed record SwitcherSettings
{
    public double Opacity { get; init; } = 0.95;
    public bool ShowOnTeamLaunch { get; init; }

    /// <summary>Clicking the switcher does not take focus away from the game.</summary>
    public bool DoNotStealFocus { get; init; } = true;

    /// <summary>Last position in physical pixels; null = default (right edge, one third down).</summary>
    public int? Left { get; init; }
    public int? Top { get; init; }

    /// <summary>
    /// Properties this build does not know (written by a newer one), kept so a save does not drop them. Only the
    /// serializer sets this (extension data cannot be init-only with source-generated metadata).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed record OverlaySettings
{
    /// <summary>Show a small click-through badge with slot number and account name on each game window.</summary>
    public bool ShowNameBadges { get; init; }

    /// <summary>
    /// Properties this build does not know (written by a newer one), kept so a save does not drop them. Only the
    /// serializer sets this (extension data cannot be init-only with source-generated metadata).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed record PerformanceSettings
{
    /// <summary>Put background clients in Windows efficiency mode (EcoQoS).</summary>
    public bool EfficiencyModeForBackground { get; init; }

    /// <summary>Run background clients at below-normal priority.</summary>
    public bool LowerBackgroundPriority { get; init; }

    /// <summary>
    /// Properties this build does not know (written by a newer one), kept so a save does not drop them. Only the
    /// serializer sets this (extension data cannot be init-only with source-generated metadata).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed record HotkeySettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Bindings as text (e.g. "Alt+1", "Ctrl+Alt+F9"); an empty string means unbound.
    /// Actions missing from the map use <see cref="DefaultHotkeys"/>.
    /// </summary>
    [JsonConverter(typeof(TolerantEnumKeyDictionaryConverter<HotkeyAction>))]
    public IReadOnlyDictionary<HotkeyAction, string> Bindings { get; init; } = new Dictionary<HotkeyAction, string>();

    /// <summary>
    /// Properties this build does not know (written by a newer one), kept so a save does not drop them. Only the
    /// serializer sets this (extension data cannot be init-only with source-generated metadata).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
