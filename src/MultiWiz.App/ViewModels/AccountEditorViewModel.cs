using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Games;
using MultiWiz.Core.Security;

namespace MultiWiz.App.ViewModels;

/// <summary>The add/edit account dialog. Saves the account and its password (to Windows Credential Manager) on Save.</summary>
public sealed partial class AccountEditorViewModel : ObservableObject
{
    /// <summary>Windows Credential Manager's limit for a generic credential blob (2,560 bytes of UTF-16).</summary>
    public const int MaxPasswordLength = 1280;

    private readonly Account? _existing;
    private readonly Guid _accountId;
    private readonly IAccountStore _accounts;
    private readonly ICredentialVault _vault;
    private readonly IRealmCatalog _realms;
    private readonly IInstallCatalog _installs;
    private readonly ILogger _logger;
    private string? _preferredRealmId;
    private string? _preferredInstallId;

    public AccountEditorViewModel(
        Account? existing,
        IAccountStore accounts,
        ICredentialVault vault,
        IRealmCatalog realms,
        IInstallCatalog installs,
        ILogger logger)
    {
        _existing = existing;
        _accountId = existing?.Id ?? Guid.NewGuid();
        _accounts = accounts;
        _vault = vault;
        _realms = realms;
        _installs = installs;
        _logger = logger;
        _preferredRealmId = existing?.RealmId;
        _preferredInstallId = existing?.InstallId;

        var swatches = AccentSwatch.Palette.ToList();
        var accent = existing?.AccentColor;
        var selectedSwatch = swatches.FirstOrDefault(
            swatch => string.Equals(swatch.Color, accent, StringComparison.OrdinalIgnoreCase));
        if (selectedSwatch is null && accent is not null)
        {
            selectedSwatch = new AccentSwatch(accent, "Current colour");
            swatches.Add(selectedSwatch);
        }

        Swatches = swatches;
        SelectedAccent = selectedSwatch ?? swatches[0];
        DisplayName = existing?.DisplayName ?? string.Empty;
        Username = existing?.Username ?? string.Empty;
        Notes = existing?.Notes ?? string.Empty;
        SelectedGame = GameOption.For(existing?.Game ?? GameKind.Wizard101);
    }

    /// <summary>Raised with true after a successful save, or false when the user cancels.</summary>
    public event EventHandler<bool>? CloseRequested;

    public bool IsNew => _existing is null;

    public string Title => IsNew ? "Add account" : "Edit account";

    public string SaveText => IsNew ? "Add account" : "Save changes";

    public string PasswordPlaceholder => IsNew
        ? "Needed for auto-login"
        : "Leave blank to keep the saved password";

    public IReadOnlyList<GameOption> Games => GameOption.All;

    public IReadOnlyList<AccentSwatch> Swatches { get; }

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Username { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsPasswordRevealed { get; set; }

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial GameOption? SelectedGame { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<RealmOption> Realms { get; private set; } = [];

    [ObservableProperty]
    public partial RealmOption? SelectedRealm { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<InstallOption> Installs { get; private set; } = [];

    [ObservableProperty]
    public partial InstallOption? SelectedInstall { get; set; }

    [ObservableProperty]
    public partial AccentSwatch? SelectedAccent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; private set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnSelectedGameChanged(GameOption? value)
    {
        if (value is null)
        {
            return;
        }

        // Keep the user's realm/install choice when it still applies to the newly selected game.
        _preferredRealmId = SelectedRealm?.RealmId ?? _preferredRealmId;
        _preferredInstallId = SelectedInstall?.InstallId ?? _preferredInstallId;

        var realms = _realms.ForGame(value.Game).Select(realm => new RealmOption(realm.Id, realm.DisplayName)).ToArray();
        Realms = realms;
        SelectedRealm = realms.FirstOrDefault(realm => realm.RealmId == _preferredRealmId)
            ?? realms.FirstOrDefault(realm => realm.RealmId == _realms.DefaultFor(value.Game).Id)
            ?? realms.FirstOrDefault();

        InstallOption[] installs = [InstallOption.Automatic, .. _installs.ForGame(value.Game).Select(InstallOption.From)];
        Installs = installs;
        SelectedInstall = installs.FirstOrDefault(install => install.InstallId == _preferredInstallId) ?? InstallOption.Automatic;
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;
        var displayName = DisplayName.Trim();
        var username = Username.Trim();

        if (displayName.Length == 0)
        {
            ErrorMessage = "Enter a name for this account, like \"Storm Main\".";
            return;
        }

        if (username.Length == 0)
        {
            ErrorMessage = "Enter the KingsIsle username used to log in.";
            return;
        }

        if (SelectedGame is null || SelectedRealm is null)
        {
            ErrorMessage = "Choose a game and a realm.";
            return;
        }

        if (Password.Length > MaxPasswordLength)
        {
            ErrorMessage = $"Passwords can be at most {MaxPasswordLength} characters.";
            return;
        }

        if (!SavePassword(username))
        {
            return;
        }

        var account = new Account
        {
            Id = _accountId,
            DisplayName = displayName,
            Username = username,
            Game = SelectedGame.Game,
            RealmId = SelectedRealm.RealmId,
            InstallId = SelectedInstall?.InstallId,
            AccentColor = SelectedAccent?.Color,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            SortOrder = _existing?.SortOrder ?? NextSortOrder(),
        };

        try
        {
            _accounts.Upsert(account);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Could not save account {AccountId}", _accountId);
            if (_existing is null)
            {
                // No account refers to the password saved above; don't leave it behind in Credential Manager.
                DeleteSavedPassword();
            }

            ErrorMessage = "The account could not be saved. Check that MultiWiz can write to its data folder.";
            return;
        }

        Password = string.Empty;
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        Password = string.Empty;
        CloseRequested?.Invoke(this, false);
    }

    private bool SavePassword(string username)
    {
        try
        {
            if (Password.Length > 0)
            {
                if (_vault.Save(_accountId, username, Password))
                {
                    return true;
                }

                ErrorMessage = "Windows Credential Manager did not accept the password.";
                return false;
            }

            // Keep the saved credential's user name in step when only the username changed.
            if (_existing is not null && !string.Equals(_existing.Username, username, StringComparison.Ordinal)
                && _vault.GetPassword(_accountId) is { } savedPassword)
            {
                _vault.Save(_accountId, username, savedPassword);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not save the password for account {AccountId}", _accountId);
            ErrorMessage = "The password could not be saved to Windows Credential Manager.";
            return false;
        }
    }

    private void DeleteSavedPassword()
    {
        try
        {
            _vault.Delete(_accountId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove the saved password of account {AccountId}", _accountId);
        }
    }

    private int NextSortOrder()
    {
        var accounts = _accounts.GetAll();
        return accounts.Count == 0 ? 0 : accounts.Max(account => account.SortOrder) + 1;
    }
}
