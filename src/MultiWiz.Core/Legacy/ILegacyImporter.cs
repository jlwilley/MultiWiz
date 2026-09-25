using MultiWiz.Core.Games;

namespace MultiWiz.Core.Legacy;

public sealed record LegacyAccount(string DisplayName, string Username, string Password, GameKind Game, string RealmId, bool WasPlainText);

public sealed record LegacySettings(bool? DarkMode, int? LoginWaitSeconds, bool? MuteWhenUnfocused, int? UnmuteVolume, double? SwitcherOpacity);

public sealed record LegacyImportPreview(IReadOnlyList<LegacyAccount> Accounts, LegacySettings Settings);

/// <summary>Reads MultiWiz 3 data (%AppData%\MultiWiz\config.txt and settings.txt) and imports it. Never changes the v3 files.</summary>
public interface ILegacyImporter
{
    /// <summary>Null when there is no v3 config file.</summary>
    LegacyImportPreview? ReadPreview();

    /// <summary>Adds the accounts (and passwords) and maps settings; marks the import as handled. Returns accounts added.</summary>
    int Apply(LegacyImportPreview preview);

    /// <summary>Marks the import as handled without importing.</summary>
    void Decline();
}
