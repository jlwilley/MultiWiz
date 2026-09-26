using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MultiWiz.App.ViewModels;

/// <summary>One slot of a team: slot N is focused by the "Focus slot N" hotkey while the team is active.</summary>
public sealed partial class TeamSlotViewModel : ObservableObject
{
    private readonly TeamEditorViewModel _owner;

    public TeamSlotViewModel(TeamEditorViewModel owner, Guid accountId)
    {
        _owner = owner;
        AccountId = accountId;
    }

    public Guid AccountId { get; }

    [ObservableProperty]
    public partial int SlotNumber { get; set; }

    [ObservableProperty]
    public partial string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? AccentColor { get; set; }

    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveUpCommand))]
    public partial bool CanMoveUp { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveDownCommand))]
    public partial bool CanMoveDown { get; set; }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => _owner.MoveSlot(this, -1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => _owner.MoveSlot(this, +1);

    [RelayCommand]
    private void Remove() => _owner.RemoveSlot(this);
}
