using CommunityToolkit.Mvvm.Input;

namespace MultiWiz.App.ViewModels;

public enum MessageKind
{
    Information,
    Question,
    Error,
    Danger,
}

/// <summary>A simple message, confirmation or error dialog.</summary>
public sealed partial class MessageDialogViewModel
{
    public MessageDialogViewModel(
        string title, string message, string? details, string confirmText, string? cancelText, MessageKind kind)
    {
        Title = title;
        Message = message;
        Details = details;
        ConfirmText = confirmText;
        CancelText = cancelText;
        Kind = kind;
    }

    /// <summary>Raised with true when confirmed, false when cancelled.</summary>
    public event EventHandler<bool>? CloseRequested;

    public string Title { get; }

    public string Message { get; }

    public string? Details { get; }

    public bool HasDetails => !string.IsNullOrWhiteSpace(Details);

    public string ConfirmText { get; }

    public string? CancelText { get; }

    public bool HasCancel => CancelText is not null;

    public MessageKind Kind { get; }

    public bool IsDestructive => Kind == MessageKind.Danger;

    public bool IsProblem => Kind is MessageKind.Error or MessageKind.Danger;

    public bool IsInformational => !IsProblem;

    [RelayCommand]
    private void Confirm() => CloseRequested?.Invoke(this, true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);
}
