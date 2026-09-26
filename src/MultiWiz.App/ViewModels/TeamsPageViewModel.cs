using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiWiz.App.Services;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Games;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Switching;
using MultiWiz.Core.Teams;

namespace MultiWiz.App.ViewModels;

/// <summary>The Teams page: team list, the editor for the selected team, and launch/arrange/activate actions.</summary>
public sealed partial class TeamsPageViewModel : ObservableObject, IDisposable
{
    private readonly ITeamStore _teams;
    private readonly IAccountStore _accounts;
    private readonly IRealmCatalog _realms;
    private readonly ITeamLauncher _launcher;
    private readonly IClientSwitcher _switcher;
    private readonly ISettingsStore _settings;
    private readonly ClientActions _actions;
    private readonly IDialogService _dialogs;
    private readonly StatusService _status;
    private readonly UiDebouncer _saveDebouncer = new(TimeSpan.FromMilliseconds(400));
    private readonly UiCoalescer _reloadTeams;
    private readonly UiCoalescer _refreshAccounts;
    private readonly UiCoalescer _refreshActive;

    public TeamsPageViewModel(
        ITeamStore teams,
        IAccountStore accounts,
        IRealmCatalog realms,
        ITeamLauncher launcher,
        IClientSwitcher switcher,
        ISettingsStore settings,
        ClientActions actions,
        IDialogService dialogs,
        StatusService status)
    {
        _teams = teams;
        _accounts = accounts;
        _realms = realms;
        _launcher = launcher;
        _switcher = switcher;
        _settings = settings;
        _actions = actions;
        _dialogs = dialogs;
        _status = status;
        _reloadTeams = new UiCoalescer(ReloadTeams);
        _refreshAccounts = new UiCoalescer(() => Editor?.RefreshAccounts());
        _refreshActive = new UiCoalescer(RefreshActiveFlags);

        ReloadTeams();
        SelectedTeam = Teams.FirstOrDefault();

        _teams.Changed += OnTeamsChanged;
        _accounts.Changed += OnAccountsChanged;
        _switcher.Changed += OnSwitcherChanged;
    }

    public ObservableCollection<TeamItemViewModel> Teams { get; } = [];

    [ObservableProperty]
    public partial TeamItemViewModel? SelectedTeam { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor))]
    [NotifyCanExecuteChangedFor(
        nameof(LaunchTeamCommand), nameof(ArrangeNowCommand), nameof(ToggleActiveCommand), nameof(DeleteTeamCommand))]
    public partial TeamEditorViewModel? Editor { get; private set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveButtonText))]
    public partial bool IsEditorTeamActive { get; private set; }

    public bool HasEditor => Editor is not null;

    public string ActiveButtonText => IsEditorTeamActive ? "Stop following" : "Set as active";

    /// <summary>Saves any edit still waiting in the debounce window (on shutdown).</summary>
    public void FlushPendingChanges() => _saveDebouncer.Flush();

    public void Dispose()
    {
        _teams.Changed -= OnTeamsChanged;
        _accounts.Changed -= OnAccountsChanged;
        _switcher.Changed -= OnSwitcherChanged;
        _saveDebouncer.Dispose();
    }

    partial void OnSelectedTeamChanged(TeamItemViewModel? value)
    {
        _saveDebouncer.Flush();
        var team = value is null ? null : _teams.Find(value.Id);
        Editor = team is null ? null : new TeamEditorViewModel(team, _accounts, _realms, ScheduleSave);
        RefreshActiveFlags();
    }

    [RelayCommand]
    private void NewTeam()
    {
        _saveDebouncer.Flush();
        var existing = _teams.GetAll();
        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = UniqueName(existing),
            LayoutId = BuiltInLayouts.NoneId,
            SortOrder = existing.Count == 0 ? 0 : existing.Max(t => t.SortOrder) + 1,
        };

        _teams.Upsert(team);
        ReloadTeams();
        SelectedTeam = Teams.FirstOrDefault(item => item.Id == team.Id);
    }

    [RelayCommand(CanExecute = nameof(HasEditor))]
    private async Task DeleteTeamAsync()
    {
        if (Editor is not { } editor)
        {
            return;
        }

        var name = editor.BuildTeam().Name;
        var confirmed = await _dialogs.ConfirmAsync(
            $"Delete {name}?",
            "The team is removed. Its accounts are kept and any running clients stay open.",
            "Delete team",
            isDestructive: true);
        if (!confirmed)
        {
            return;
        }

        _saveDebouncer.Cancel();
        var teamId = editor.TeamId;
        if (_switcher.ActiveTeamId == teamId)
        {
            _switcher.SetActiveTeam(null);
        }

        _settings.Update(settings => settings.LastTeamId == teamId ? settings with { LastTeamId = null } : settings);

        var index = Teams.ToList().FindIndex(item => item.Id == teamId);
        _teams.Remove(teamId);
        ReloadTeams();
        SelectedTeam = Teams.Count == 0 ? null : Teams[Math.Clamp(index, 0, Teams.Count - 1)];
        _status.Show($"Deleted {name}.");
    }

    [RelayCommand(CanExecute = nameof(HasEditor))]
    private void LaunchTeam()
    {
        if (Editor is { } editor)
        {
            _saveDebouncer.Flush();
            _actions.LaunchTeam(editor.TeamId);
        }
    }

    [RelayCommand(CanExecute = nameof(HasEditor))]
    private async Task ArrangeNowAsync()
    {
        if (Editor is not { } editor)
        {
            return;
        }

        _saveDebouncer.Flush();
        if (!editor.CanResize)
        {
            _status.Show("Pick a window layout first; this team leaves windows where the game opens them.");
            return;
        }

        // Moving another process's window waits for that window's thread, so a busy client must not freeze the UI.
        var teamId = editor.TeamId;
        var moved = await Task.Run(() => _launcher.ArrangeNow(teamId));
        _status.Show(moved switch
        {
            0 => "None of this team's clients are running with a window yet.",
            1 => "Arranged 1 window.",
            _ => $"Arranged {moved} windows.",
        });
    }

    [RelayCommand(CanExecute = nameof(HasEditor))]
    private void ToggleActive()
    {
        if (Editor is not { } editor)
        {
            return;
        }

        _saveDebouncer.Flush();
        if (_switcher.ActiveTeamId == editor.TeamId)
        {
            SetActiveTeam(null);
            _status.Show("The switcher now lists clients in account order.");
        }
        else
        {
            SetActiveTeam(editor.TeamId);
            _status.Show($"The switcher and slot hotkeys now follow {editor.BuildTeam().Name}.");
        }

        RefreshActiveFlags();
    }

    /// <summary>Changes the switcher's team and remembers it, because the switcher restores its order from settings at startup.</summary>
    private void SetActiveTeam(Guid? teamId)
    {
        _switcher.SetActiveTeam(teamId);
        _settings.Update(settings => settings.LastTeamId == teamId ? settings : settings with { LastTeamId = teamId });
    }

    private void ScheduleSave() => _saveDebouncer.Schedule(SaveEditor);

    private void SaveEditor()
    {
        if (Editor is { } editor && _teams.Find(editor.TeamId) is not null)
        {
            _teams.Upsert(editor.BuildTeam());
        }
    }

    private void OnTeamsChanged(object? sender, EventArgs e) => _reloadTeams.Request();

    private void OnAccountsChanged(object? sender, EventArgs e)
    {
        _reloadTeams.Request();
        _refreshAccounts.Request();
    }

    private void OnSwitcherChanged(object? sender, EventArgs e) => _refreshActive.Request();

    private void ReloadTeams()
    {
        var teams = _teams.GetAll();
        var existing = Teams.ToDictionary(item => item.Id);
        var ordered = new List<TeamItemViewModel>(teams.Count);
        foreach (var team in teams)
        {
            if (!existing.TryGetValue(team.Id, out var item))
            {
                item = new TeamItemViewModel(team.Id);
            }

            item.Name = team.Name;
            item.Summary = Describe(team);
            ordered.Add(item);
        }

        CollectionSync.Apply(Teams, ordered);
        IsEmpty = Teams.Count == 0;
        if (SelectedTeam is not null && !Teams.Contains(SelectedTeam))
        {
            SelectedTeam = Teams.FirstOrDefault();
        }

        RefreshActiveFlags();
    }

    private void RefreshActiveFlags()
    {
        var activeId = _switcher.ActiveTeamId;
        foreach (var item in Teams)
        {
            item.IsActive = item.Id == activeId;
        }

        IsEditorTeamActive = Editor is not null && Editor.TeamId == activeId;
    }

    private string Describe(Team team)
    {
        var count = team.AccountIds.Count(id => _accounts.Find(id) is not null);
        var accounts = count == 1 ? "1 account" : $"{count} accounts";
        var layout = BuiltInLayouts.Find(team.LayoutId)?.Name ?? "Custom layout";
        return $"{accounts} · {layout}";
    }

    private static string UniqueName(IReadOnlyList<Team> existing)
    {
        var names = existing.Select(team => team.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = "New team";
        for (var n = 2; names.Contains(name); n++)
        {
            name = $"New team {n}";
        }

        return name;
    }
}
