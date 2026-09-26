using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MultiWiz.App.Services;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Games;
using MultiWiz.Core.Security;
using MultiWiz.Core.Sessions;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Switching;
using MultiWiz.Core.Teams;

namespace MultiWiz.App.ViewModels;

/// <summary>The Accounts page: the account list, per-row actions, multi-select launch and the add/edit dialog.</summary>
public sealed partial class AccountsPageViewModel : ObservableObject, IDisposable
{
    private readonly IAccountStore _accounts;
    private readonly IRealmCatalog _realms;
    private readonly IInstallCatalog _installs;
    private readonly ITeamStore _teams;
    private readonly ICredentialVault _vault;
    private readonly ISessionManager _sessions;
    private readonly IClientSwitcher _switcher;
    private readonly ISettingsStore _settings;
    private readonly ClientActions _actions;
    private readonly IDialogService _dialogs;
    private readonly LegacyImportService _legacyImport;
    private readonly StatusService _status;
    private readonly ILogger<AccountsPageViewModel> _logger;
    private readonly UiCoalescer _reload;

    public AccountsPageViewModel(
        IAccountStore accounts,
        IRealmCatalog realms,
        IInstallCatalog installs,
        ITeamStore teams,
        ICredentialVault vault,
        ISessionManager sessions,
        IClientSwitcher switcher,
        ISettingsStore settings,
        ClientActions actions,
        IDialogService dialogs,
        LegacyImportService legacyImport,
        StatusService status,
        ILogger<AccountsPageViewModel> logger)
    {
        _accounts = accounts;
        _realms = realms;
        _installs = installs;
        _teams = teams;
        _vault = vault;
        _sessions = sessions;
        _switcher = switcher;
        _settings = settings;
        _actions = actions;
        _dialogs = dialogs;
        _legacyImport = legacyImport;
        _status = status;
        _logger = logger;
        _reload = new UiCoalescer(Reload);

        Reload();

        _accounts.Changed += OnAccountsChanged;
        _settings.Changed += OnSettingsChanged;
        _sessions.SessionChanged += OnSessionChanged;
    }

    public ObservableCollection<AccountItemViewModel> Accounts { get; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(LaunchSelectedText))]
    [NotifyCanExecuteChangedFor(nameof(LaunchSelectedCommand))]
    public partial int SelectedCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectAllText))]
    public partial bool AreAllSelected { get; private set; }

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasLegacyData { get; private set; }

    public bool HasSelection => SelectedCount > 0;

    public string LaunchSelectedText => SelectedCount > 0 ? $"Launch selected ({SelectedCount})" : "Launch selected";

    public string SelectAllText => AreAllSelected ? "Clear selection" : "Select all";

    public void Dispose()
    {
        _accounts.Changed -= OnAccountsChanged;
        _settings.Changed -= OnSettingsChanged;
        _sessions.SessionChanged -= OnSessionChanged;
    }

    internal void Launch(AccountItemViewModel item) => _actions.Launch(item.Id);

    internal void Stop(AccountItemViewModel item) => _actions.Stop(item.Id);

    internal void Focus(AccountItemViewModel item)
    {
        if (!_switcher.Focus(item.Id))
        {
            _status.Show($"{item.DisplayName} has no game window to focus yet.");
        }
    }

    internal async Task EditAsync(AccountItemViewModel item)
    {
        var current = _accounts.Find(item.Id) ?? item.Account;
        var editor = CreateEditor(current);
        if (await _dialogs.EditAccountAsync(editor))
        {
            _status.Show($"Saved {editor.DisplayName.Trim()}.");
        }
    }

    internal async Task DeleteAsync(AccountItemViewModel item)
    {
        var hasRunningClient = _sessions.Find(item.Id) is { IsAlive: true };
        var confirmed = await _dialogs.ConfirmAsync(
            $"Delete {item.DisplayName}?",
            "The account is removed from MultiWiz and from every team, and its saved password is deleted from " +
            "Windows Credential Manager. Your KingsIsle account itself is not affected." +
            (hasRunningClient ? " Its running game client will be closed." : string.Empty),
            "Delete account",
            isDestructive: true);
        if (!confirmed)
        {
            return;
        }

        // A client whose account is gone could no longer be stopped from this page, so close it with the account.
        if (_actions.Stop(item.Id))
        {
            _logger.LogInformation("Closed the running client of deleted account {AccountId}", item.Id);
        }

        _accounts.Remove(item.Id);
        if (!_vault.Delete(item.Id))
        {
            _logger.LogDebug("No saved password to delete for account {AccountId}", item.Id);
        }

        _teams.RemoveAccountEverywhere(item.Id);
        _status.Show($"Deleted {item.DisplayName}.");
    }

    internal void Move(AccountItemViewModel item, int offset)
    {
        var index = Accounts.IndexOf(item);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Accounts.Count)
        {
            return;
        }

        Accounts.Move(index, target);
        UpdateMoveFlags();
        _accounts.Reorder(Accounts.Select(account => account.Id).ToArray());
    }

    internal void OnSelectionChanged()
    {
        SelectedCount = Accounts.Count(account => account.IsSelected);
        AreAllSelected = Accounts.Count > 0 && SelectedCount == Accounts.Count;
    }

    [RelayCommand]
    private async Task AddAccountAsync()
    {
        var editor = CreateEditor(existing: null);
        if (await _dialogs.EditAccountAsync(editor))
        {
            _status.Show($"Added {editor.DisplayName.Trim()}.");
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void LaunchSelected()
    {
        var selected = Accounts.Where(account => account.IsSelected).Select(account => account.Id).ToArray();
        foreach (var account in Accounts)
        {
            account.IsSelected = false;
        }

        _actions.LaunchMany(selected);
    }

    [RelayCommand]
    private void ToggleSelectAll()
    {
        var select = !AreAllSelected;
        foreach (var account in Accounts)
        {
            account.IsSelected = select;
        }
    }

    [RelayCommand]
    private async Task ImportLegacyAsync()
    {
        await _legacyImport.OfferAsync(userRequested: true);
        HasLegacyData = _legacyImport.HasLegacyData;
    }

    private AccountEditorViewModel CreateEditor(Account? existing) =>
        new(existing, _accounts, _vault, _realms, _installs, _logger);

    private void OnAccountsChanged(object? sender, EventArgs e) => _reload.Request();

    private void OnSettingsChanged(object? sender, AppSettings settings) => _reload.Request();

    private void OnSessionChanged(object? sender, ClientSession session) =>
        Dispatcher.UIThread.Post(() => ApplySession(session));

    private void ApplySession(ClientSession session)
    {
        var item = Accounts.FirstOrDefault(account => account.Id == session.AccountId);
        if (item is null)
        {
            return;
        }

        item.ApplySession(session);
        UpdateSummary();
    }

    private void Reload()
    {
        var accounts = _accounts.GetAll();
        var existing = Accounts.ToDictionary(account => account.Id);
        var ordered = new List<AccountItemViewModel>(accounts.Count);
        foreach (var account in accounts)
        {
            var subtitle = Describe(account);
            if (existing.TryGetValue(account.Id, out var item))
            {
                item.Update(account, subtitle);
            }
            else
            {
                item = new AccountItemViewModel(this, account, subtitle);
                item.ApplySession(_sessions.Find(account.Id));
            }

            ordered.Add(item);
        }

        CollectionSync.Apply(Accounts, ordered);
        IsEmpty = Accounts.Count == 0;
        HasLegacyData = _legacyImport.HasLegacyData;
        UpdateMoveFlags();
        UpdateSummary();
        OnSelectionChanged();
    }

    private string Describe(Account account)
    {
        var realm = _realms.Find(account.RealmId)?.DisplayName ?? account.RealmId;
        return $"{realm} · {account.Username}";
    }

    private void UpdateMoveFlags()
    {
        for (var i = 0; i < Accounts.Count; i++)
        {
            Accounts[i].CanMoveUp = i > 0;
            Accounts[i].CanMoveDown = i < Accounts.Count - 1;
        }
    }

    private void UpdateSummary()
    {
        var running = Accounts.Count(account => account.IsAlive);
        var total = Accounts.Count;
        var accountsText = total == 1 ? "1 account" : $"{total} accounts";
        Summary = running == 0 ? accountsText : $"{accountsText} · {running} running";
    }
}
