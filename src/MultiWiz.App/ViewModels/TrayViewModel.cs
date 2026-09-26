using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiWiz.App.Services;
using MultiWiz.Core.Sessions;
using MultiWiz.Core.Teams;

namespace MultiWiz.App.ViewModels;

/// <summary>
/// Data context of the tray icon (Application.DataContext): show/hide windows, launch a team, stop clients, quit.
/// The "Launch team" submenu is a <see cref="NativeMenu"/> rebuilt whenever the teams change.
/// </summary>
public sealed partial class TrayViewModel : ObservableObject, IDisposable
{
    private readonly WindowCoordinator _windows;
    private readonly ClientActions _actions;
    private readonly ITeamStore _teams;
    private readonly ISessionManager _sessions;
    private readonly UiCoalescer _teamsRefresh;
    private readonly UiCoalescer _toolTipRefresh;

    public TrayViewModel(WindowCoordinator windows, ClientActions actions, ITeamStore teams, ISessionManager sessions)
    {
        _windows = windows;
        _actions = actions;
        _teams = teams;
        _sessions = sessions;
        _teamsRefresh = new UiCoalescer(RebuildTeamsMenu);
        _toolTipRefresh = new UiCoalescer(RefreshToolTip);

        RebuildTeamsMenu();
        RefreshToolTip();

        _teams.Changed += OnTeamsChanged;
        _sessions.SessionChanged += OnSessionChanged;
    }

    public NativeMenu TeamsMenu { get; } = new();

    [ObservableProperty]
    public partial string ToolTipText { get; private set; } = "MultiWiz";

    public void Dispose()
    {
        _teams.Changed -= OnTeamsChanged;
        _sessions.SessionChanged -= OnSessionChanged;
    }

    [RelayCommand]
    private void ShowMainWindow() => _windows.ShowMainWindow();

    [RelayCommand]
    private void ToggleSwitcher() => _windows.ToggleSwitcher();

    [RelayCommand]
    private void ToggleCommandCenter() => _windows.ToggleCommandCenter();

    [RelayCommand]
    private void StopAll() => _actions.StopAll();

    [RelayCommand]
    private void Quit() => _windows.Quit();

    private void OnTeamsChanged(object? sender, EventArgs e) => _teamsRefresh.Request();

    private void OnSessionChanged(object? sender, ClientSession session) => _toolTipRefresh.Request();

    private void RebuildTeamsMenu()
    {
        TeamsMenu.Items.Clear();
        var teams = _teams.GetAll();
        if (teams.Count == 0)
        {
            TeamsMenu.Items.Add(new NativeMenuItem { Header = "No teams yet", IsEnabled = false });
            return;
        }

        foreach (var team in teams)
        {
            // A Click handler rather than a command: NativeMenuItem only queries CanExecute when Command changes, and
            // a Guid command rejects the null parameter it sees at that moment, which would leave the item disabled.
            var teamId = team.Id;
            var item = new NativeMenuItem { Header = team.Name };
            item.Click += (_, _) => _actions.LaunchTeam(teamId);
            TeamsMenu.Items.Add(item);
        }
    }

    private void RefreshToolTip()
    {
        var running = _sessions.Sessions.Count(session => session.IsAlive);
        ToolTipText = running switch
        {
            0 => "MultiWiz",
            1 => "MultiWiz · 1 client running",
            _ => $"MultiWiz · {running} clients running",
        };
    }
}
