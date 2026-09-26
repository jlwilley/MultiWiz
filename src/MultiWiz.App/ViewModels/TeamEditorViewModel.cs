using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Games;
using MultiWiz.Core.Teams;

namespace MultiWiz.App.ViewModels;

/// <summary>
/// Edits one team: name, ordered account slots, window layout and resize option. Every edit invokes the
/// change callback; the Teams page debounces those into a save.
/// </summary>
public sealed partial class TeamEditorViewModel : ObservableObject
{
    private readonly IAccountStore _accounts;
    private readonly IRealmCatalog _realms;
    private readonly Action _changed;
    private readonly int _sortOrder;
    private readonly bool _initialized;

    public TeamEditorViewModel(Team team, IAccountStore accounts, IRealmCatalog realms, Action changed)
    {
        _accounts = accounts;
        _realms = realms;
        _changed = changed;
        TeamId = team.Id;
        _sortOrder = team.SortOrder;

        Name = team.Name;
        ResizeWindows = team.ResizeWindows;
        SelectedLayout = Layouts.FirstOrDefault(layout => string.Equals(layout.Layout.Id, team.LayoutId, StringComparison.OrdinalIgnoreCase))
            ?? Layouts[0];
        foreach (var accountId in team.AccountIds.Distinct())
        {
            Slots.Add(new TeamSlotViewModel(this, accountId));
        }

        RefreshAccounts(notify: false);
        _initialized = true;
    }

    public Guid TeamId { get; }

    public ObservableCollection<TeamSlotViewModel> Slots { get; } = [];

    public ObservableCollection<AccountOption> AvailableAccounts { get; } = [];

    public IReadOnlyList<LayoutOption> Layouts { get; } = BuiltInLayouts.All.Select(layout => new LayoutOption(layout)).ToArray();

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial LayoutOption? SelectedLayout { get; set; }

    [ObservableProperty]
    public partial bool ResizeWindows { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddSlotCommand))]
    public partial AccountOption? AccountToAdd { get; set; }

    [ObservableProperty]
    public partial bool HasSlots { get; private set; }

    [ObservableProperty]
    public partial bool HasAvailableAccounts { get; private set; }

    [ObservableProperty]
    public partial string SlotSummary { get; private set; } = string.Empty;

    public bool CanResize => SelectedLayout is { Layout.Id: not BuiltInLayouts.NoneId };

    public Team BuildTeam() => new()
    {
        Id = TeamId,
        Name = string.IsNullOrWhiteSpace(Name) ? "Untitled team" : Name.Trim(),
        AccountIds = Slots.Select(slot => slot.AccountId).ToArray(),
        LayoutId = SelectedLayout?.Layout.Id ?? BuiltInLayouts.NoneId,
        ResizeWindows = ResizeWindows,
        SortOrder = _sortOrder,
    };

    /// <summary>Refreshes names and colours, drops slots whose account was deleted, and rebuilds the add list.</summary>
    public void RefreshAccounts() => RefreshAccounts(notify: true);

    internal void MoveSlot(TeamSlotViewModel slot, int offset)
    {
        var index = Slots.IndexOf(slot);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Slots.Count)
        {
            return;
        }

        Slots.Move(index, target);
        RenumberSlots();
        NotifyChanged();
    }

    internal void RemoveSlot(TeamSlotViewModel slot)
    {
        if (Slots.Remove(slot))
        {
            RenumberSlots();
            RebuildAvailableAccounts();
            NotifyChanged();
        }
    }

    partial void OnNameChanged(string value) => NotifyChanged();

    partial void OnSelectedLayoutChanged(LayoutOption? value)
    {
        OnPropertyChanged(nameof(CanResize));
        NotifyChanged();
    }

    partial void OnResizeWindowsChanged(bool value) => NotifyChanged();

    [RelayCommand(CanExecute = nameof(CanAddSlot))]
    private void AddSlot()
    {
        if (AccountToAdd is not { } option || Slots.Any(slot => slot.AccountId == option.AccountId))
        {
            return;
        }

        var slot = new TeamSlotViewModel(this, option.AccountId);
        Slots.Add(slot);
        RefreshAccounts(notify: true);
    }

    private bool CanAddSlot() => AccountToAdd is not null;

    private void RefreshAccounts(bool notify)
    {
        var removed = false;
        for (var i = Slots.Count - 1; i >= 0; i--)
        {
            var slot = Slots[i];
            var account = _accounts.Find(slot.AccountId);
            if (account is null)
            {
                Slots.RemoveAt(i);
                removed = true;
                continue;
            }

            slot.DisplayName = account.DisplayName;
            slot.AccentColor = account.AccentColor;
            slot.Detail = _realms.Find(account.RealmId)?.DisplayName ?? account.RealmId;
        }

        RenumberSlots();
        RebuildAvailableAccounts();
        if (notify || removed)
        {
            NotifyChanged();
        }
    }

    private void RebuildAvailableAccounts()
    {
        var used = Slots.Select(slot => slot.AccountId).ToHashSet();
        AvailableAccounts.Clear();
        foreach (var account in _accounts.GetAll().Where(account => !used.Contains(account.Id)))
        {
            AvailableAccounts.Add(new AccountOption(account.Id, account.DisplayName));
        }

        AccountToAdd = AvailableAccounts.FirstOrDefault();
        HasAvailableAccounts = AvailableAccounts.Count > 0;
    }

    private void RenumberSlots()
    {
        for (var i = 0; i < Slots.Count; i++)
        {
            Slots[i].SlotNumber = i + 1;
            Slots[i].CanMoveUp = i > 0;
            Slots[i].CanMoveDown = i < Slots.Count - 1;
        }

        HasSlots = Slots.Count > 0;
        SlotSummary = Slots.Count switch
        {
            0 => "No accounts yet",
            1 => "1 account",
            _ => $"{Slots.Count} accounts",
        };
    }

    private void NotifyChanged()
    {
        if (_initialized)
        {
            _changed();
        }
    }
}
