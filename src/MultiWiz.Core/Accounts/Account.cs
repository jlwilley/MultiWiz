using System.Text.Json;
using System.Text.Json.Serialization;
using MultiWiz.Core.Games;

namespace MultiWiz.Core.Accounts;

/// <summary>
/// A saved game account. The password is NOT stored here; it lives in
/// <see cref="Security.ICredentialVault"/> keyed by <see cref="Id"/>.
/// </summary>
public sealed record Account
{
    public required Guid Id { get; init; }

    /// <summary>Name shown in the UI and on overlays (e.g. "Storm Main").</summary>
    public required string DisplayName { get; init; }

    /// <summary>KingsIsle login name.</summary>
    public required string Username { get; init; }

    public GameKind Game { get; init; } = GameKind.Wizard101;

    /// <summary>Id of a built-in or custom <see cref="Realm"/>.</summary>
    public string RealmId { get; init; } = "w101-us";

    /// <summary>Id of a <see cref="GameInstall"/>; null means "use the preferred install for this game".</summary>
    public string? InstallId { get; init; }

    /// <summary>Optional accent color as "#RRGGBB" used for badges and switcher entries.</summary>
    public string? AccentColor { get; init; }

    public string? Notes { get; init; }

    /// <summary>Position in the account list (ascending).</summary>
    public int SortOrder { get; init; }

    /// <summary>
    /// Properties this build does not know (written by a newer one), kept so a save does not drop them. Only the
    /// serializer sets this (extension data cannot be init-only with source-generated metadata).
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
