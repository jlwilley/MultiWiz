using Avalonia.Controls;
using Avalonia.Platform.Storage;
using MultiWiz.App.ViewModels;
using MultiWiz.App.Views.Dialogs;

namespace MultiWiz.App.Services;

public sealed class DialogService : IDialogService
{
    private readonly WindowCoordinator _windows;

    public DialogService(WindowCoordinator windows)
    {
        _windows = windows;
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false) =>
        ShowMessageDialogAsync(new MessageDialogViewModel(
            title, message, details: null, confirmText, cancelText: "Cancel",
            isDestructive ? MessageKind.Danger : MessageKind.Question));

    public Task ShowMessageAsync(string title, string message) =>
        ShowMessageDialogAsync(new MessageDialogViewModel(
            title, message, details: null, confirmText: "OK", cancelText: null, MessageKind.Information));

    public Task ShowErrorAsync(string title, string message, string? details = null) =>
        ShowMessageDialogAsync(new MessageDialogViewModel(
            title, message, details, confirmText: "OK", cancelText: null, MessageKind.Error));

    public Task<bool> EditAccountAsync(AccountEditorViewModel editor) =>
        ShowDialogAsync<bool>(new AccountEditorWindow { DataContext = editor });

    public Task<LegacyImportChoice> ShowLegacyImportAsync(LegacyImportViewModel viewModel) =>
        ShowDialogAsync<LegacyImportChoice>(new LegacyImportWindow { DataContext = viewModel });

    public async Task<string?> PickFolderAsync(string title)
    {
        var owner = _windows.GetDialogOwner();
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private Task<bool> ShowMessageDialogAsync(MessageDialogViewModel viewModel) =>
        ShowDialogAsync<bool>(new MessageDialog { DataContext = viewModel });

    private Task<TResult> ShowDialogAsync<TResult>(Window dialog) => dialog.ShowDialog<TResult>(_windows.GetDialogOwner());
}
