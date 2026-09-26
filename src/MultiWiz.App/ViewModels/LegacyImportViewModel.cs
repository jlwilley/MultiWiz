using CommunityToolkit.Mvvm.Input;
using MultiWiz.Core.Games;
using MultiWiz.Core.Legacy;

namespace MultiWiz.App.ViewModels;

/// <summary>What the user chose in the MultiWiz 3 import prompt. Closing the window means <see cref="Later"/>.</summary>
public enum LegacyImportChoice
{
    Later = 0,
    Import = 1,
    Decline = 2,
}

/// <param name="CouldNotDecrypt">
/// The v3 login details were encrypted for another Windows user or PC, so the account comes in without them.
/// </param>
public sealed record LegacyAccountRow(string DisplayName, string Username, string Realm, bool HasPassword, bool CouldNotDecrypt)
{
    public bool HasUsername => Username.Length > 0;

    /// <summary>No password to import, for a reason other than <see cref="CouldNotDecrypt"/> (which has its own note).</summary>
    public bool ShowNoPassword => !HasPassword && !CouldNotDecrypt;
}

/// <summary>The first-run prompt listing the MultiWiz 3 accounts (and settings) that would be imported.</summary>
public sealed partial class LegacyImportViewModel
{
    public LegacyImportViewModel(LegacyImportPreview preview, IRealmCatalog realms)
    {
        Accounts = preview.Accounts
            .Select(account => new LegacyAccountRow(
                account.DisplayName,
                account.Username,
                realms.Find(account.RealmId)?.DisplayName ?? account.RealmId,
                !string.IsNullOrEmpty(account.Password),
                account.CouldNotDecrypt))
            .ToArray();
        SettingsSummary = DescribeSettings(preview.Settings);
    }

    /// <summary>Raised with the user's choice; the dialog closes with it.</summary>
    public event EventHandler<LegacyImportChoice>? CloseRequested;

    public IReadOnlyList<LegacyAccountRow> Accounts { get; }

    /// <summary>The v3 settings that will be carried over, one bulleted line each (empty when there are none).</summary>
    public string SettingsSummary { get; }

    public bool HasSettings => SettingsSummary.Length > 0;

    public string Heading => Accounts.Count == 1
        ? "Import your account from MultiWiz 3?"
        : $"Import your {Accounts.Count} accounts from MultiWiz 3?";

    [RelayCommand]
    private void Import() => CloseRequested?.Invoke(this, LegacyImportChoice.Import);

    [RelayCommand]
    private void Decline() => CloseRequested?.Invoke(this, LegacyImportChoice.Decline);

    [RelayCommand]
    private void Later() => CloseRequested?.Invoke(this, LegacyImportChoice.Later);

    private static string DescribeSettings(LegacySettings settings)
    {
        var lines = new List<string>();
        if (settings.DarkMode is { } dark)
        {
            lines.Add(dark ? "Dark theme" : "Light theme");
        }

        if (settings.LoginWaitSeconds is { } wait)
        {
            lines.Add($"Wait {Math.Clamp(wait, 2, 30)} seconds before typing the login");
        }

        if (settings.MuteWhenUnfocused is { } mute)
        {
            lines.Add(mute ? "Mute clients in the background" : "Leave background clients' volume alone");
        }

        if (settings.UnmuteVolume is { } volume)
        {
            lines.Add($"Focused client volume {Math.Clamp(volume, 0, 100)}%");
        }

        if (settings.SwitcherOpacity is { } opacity)
        {
            lines.Add($"Switcher opacity {Math.Clamp(opacity, 0.2, 1.0) * 100:0}%");
        }

        return string.Join(Environment.NewLine, lines.Select(line => $"• {line}"));
    }
}
