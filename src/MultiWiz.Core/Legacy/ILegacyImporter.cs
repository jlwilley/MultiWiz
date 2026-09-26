using MultiWiz.Core.Games;

namespace MultiWiz.Core.Legacy;

/// <param name="CouldNotDecrypt">
/// The v3 login details were encrypted for another Windows user or PC (a copied or restored profile, or a reset Windows
/// password), so <see cref="Username"/> and/or <see cref="Password"/> are empty and must be re-entered after the import.
/// </param>
public sealed record LegacyAccount(
    string DisplayName,
    string Username,
    string Password,
    GameKind Game,
    string RealmId,
    bool WasPlainText,
    bool CouldNotDecrypt = false);

public sealed record LegacySettings(bool? DarkMode, int? LoginWaitSeconds, bool? MuteWhenUnfocused, int? UnmuteVolume, double? SwitcherOpacity);

public sealed record LegacyImportPreview(IReadOnlyList<LegacyAccount> Accounts, LegacySettings Settings);

/// <param name="Added">Accounts added.</param>
/// <param name="NeedLoginDetails">
/// Added accounts whose username or password must be re-entered: they could not be decrypted on this PC, or the
/// password could not be saved.
/// </param>
public sealed record LegacyImportResult(int Added, int NeedLoginDetails);

/// <summary>Reads MultiWiz 3 data (%AppData%\MultiWiz\config.txt and settings.txt) and imports it. Never changes the v3 files.</summary>
public interface ILegacyImporter
{
    /// <summary>Null when there is no v3 config file.</summary>
    LegacyImportPreview? ReadPreview();

    /// <summary>Adds the accounts (and passwords) and maps settings; marks the import as handled. Returns accounts added.</summary>
    int Apply(LegacyImportPreview preview);

    /// <summary>Like <see cref="Apply"/>, and also reports how many added accounts need their login details re-entered.</summary>
    LegacyImportResult Import(LegacyImportPreview preview);

    /// <summary>Marks the import as handled without importing.</summary>
    void Decline();
}
