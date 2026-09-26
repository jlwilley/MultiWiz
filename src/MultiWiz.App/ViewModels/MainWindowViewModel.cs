using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiWiz.App.Services;
using MultiWiz.Core.Sessions;
using MultiWiz.Core.Settings;

namespace MultiWiz.App.ViewModels;

/// <summary>The main window shell: sidebar navigation, quick actions and the status bar.</summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly ISessionManager _sessions;
    private readonly ISettingsStore _settings;
    private readonly WindowCoordinator _windows;
    private readonly ClientActions _actions;
    private readonly UiCoalescer _runningRefresh;

    public MainWindowViewModel(
        AccountsPageViewModel accountsPage,
        TeamsPageViewModel teamsPage,
        SettingsPageViewModel settingsPage,
        AboutPageViewModel aboutPage,
        ISessionManager sessions,
        ISettingsStore settings,
        UpdateService updates,
        StatusService status,
        WindowCoordinator windows,
        ClientActions actions)
    {
        AccountsPage = accountsPage;
        TeamsPage = teamsPage;
        SettingsPage = settingsPage;
        AboutPage = aboutPage;
        Updates = updates;
        Status = status;
        _sessions = sessions;
        _settings = settings;
        _windows = windows;
        _actions = actions;
        _runningRefresh = new UiCoalescer(RefreshRunningCount);

        RefreshRunningCount();
        _sessions.SessionChanged += OnSessionChanged;
    }

    public AccountsPageViewModel AccountsPage { get; }

    public TeamsPageViewModel TeamsPage { get; }

    public SettingsPageViewModel SettingsPage { get; }

    public AboutPageViewModel AboutPage { get; }

    public UpdateService Updates { get; }

    public StatusService Status { get; }

    /// <summary>0 Accounts, 1 Teams, 2 Settings, 3 About (the sidebar order).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    public partial int SelectedPageIndex { get; set; }

    public object CurrentPage => SelectedPageIndex switch
    {
        1 => TeamsPage,
        2 => SettingsPage,
        3 => AboutPage,
        _ => AccountsPage,
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RunningText), nameof(HasRunningClients))]
    [NotifyCanExecuteChangedFor(nameof(StopAllCommand))]
    public partial int RunningCount { get; private set; }

    public bool HasRunningClients => RunningCount > 0;

    public string RunningText => RunningCount switch
    {
        0 => "No clients running",
        1 => "1 client running",
        _ => $"{RunningCount} clients running",
    };

    /// <summary>Read by the window when it is minimized.</summary>
    public bool MinimizeToTray => _settings.Current.General.MinimizeToTray;

    /// <summary>Read by the window when the user closes it.</summary>
    public bool CloseToTray => _settings.Current.General.CloseToTray;

    public void RequestQuit() => _windows.Quit();

    public void Dispose() => _sessions.SessionChanged -= OnSessionChanged;

    [RelayCommand]
    private void ToggleSwitcher() => _windows.ToggleSwitcher();

    [RelayCommand]
    private void ToggleCommandCenter() => _windows.ToggleCommandCenter();

    [RelayCommand(CanExecute = nameof(HasRunningClients))]
    private void StopAll() => _actions.StopAll();

    [RelayCommand]
    private void RestartToUpdate() => Updates.RestartToApply();

    private void OnSessionChanged(object? sender, ClientSession session) => _runningRefresh.Request();

    private void RefreshRunningCount() => RunningCount = _sessions.Sessions.Count(session => session.IsAlive);
}
