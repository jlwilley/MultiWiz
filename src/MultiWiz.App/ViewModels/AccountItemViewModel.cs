using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiWiz.Core.Accounts;
using MultiWiz.Core.Sessions;

namespace MultiWiz.App.ViewModels;

/// <summary>One row of the account list: the account plus the live state of its client.</summary>
public sealed partial class AccountItemViewModel : ObservableObject
{
    private readonly AccountsPageViewModel _owner;

    public AccountItemViewModel(AccountsPageViewModel owner, Account account, string subtitle)
    {
        _owner = owner;
        Id = account.Id;
        Account = account;
        DisplayName = account.DisplayName;
        Subtitle = subtitle;
        AccentColor = account.AccentColor;
    }

    public Guid Id { get; }

    public Account Account { get; private set; }

    [ObservableProperty]
    public partial string DisplayName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Subtitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? AccentColor { get; private set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAlive), nameof(IsRunning), nameof(IsBusy), nameof(IsFailed), nameof(StatusText), nameof(CanFocus))]
    [NotifyCanExecuteChangedFor(nameof(FocusCommand))]
    public partial ClientSessionState? State { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanFocus))]
    [NotifyCanExecuteChangedFor(nameof(FocusCommand))]
    public partial bool HasWindow { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusToolTip))]
    public partial string? ErrorText { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    public partial bool CanMoveUp { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    public partial bool CanMoveDown { get; set; }

    public bool IsAlive => State is ClientSessionState.Launching or ClientSessionState.WaitingForWindow
        or ClientSessionState.WaitingForReady or ClientSessionState.LoggingIn or ClientSessionState.Running;

    public bool IsRunning => State == ClientSessionState.Running;

    public bool IsBusy => IsAlive && !IsRunning;

    public bool IsFailed => State == ClientSessionState.Failed;

    public bool CanFocus => IsAlive && HasWindow;

    public string StatusText => State switch
    {
        ClientSessionState.Launching => "Starting",
        ClientSessionState.WaitingForWindow => "Opening",
        ClientSessionState.WaitingForReady => "Loading",
        ClientSessionState.LoggingIn => "Logging in",
        ClientSessionState.Running => "Running",
        ClientSessionState.Failed => "Failed",
        _ => "Idle",
    };

    public string StatusToolTip => ErrorText ?? "Client state";

    internal void Update(Account account, string subtitle)
    {
        Account = account;
        DisplayName = account.DisplayName;
        Subtitle = subtitle;
        AccentColor = account.AccentColor;
    }

    /// <summary>Applies a session snapshot; null (or an exited session) means the client is not running.</summary>
    internal void ApplySession(ClientSession? session)
    {
        if (session is null || session.State == ClientSessionState.Exited)
        {
            State = null;
            HasWindow = false;
            ErrorText = null;
            return;
        }

        State = session.State;
        HasWindow = session.HasWindow;
        ErrorText = session.State == ClientSessionState.Failed ? session.Error ?? "The client could not be started." : null;
    }

    partial void OnIsSelectedChanged(bool value) => _owner.OnSelectionChanged();

    [RelayCommand]
    private void Launch() => _owner.Launch(this);

    [RelayCommand]
    private void Stop() => _owner.Stop(this);

    [RelayCommand(CanExecute = nameof(CanFocus))]
    private void Focus() => _owner.Focus(this);

    [RelayCommand]
    private Task EditAsync() => _owner.EditAsync(this);

    [RelayCommand]
    private Task DeleteAsync() => _owner.DeleteAsync(this);

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => _owner.Move(this, -1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => _owner.Move(this, +1);
}
