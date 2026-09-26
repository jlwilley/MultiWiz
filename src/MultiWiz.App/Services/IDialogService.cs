using MultiWiz.App.ViewModels;

namespace MultiWiz.App.Services;

/// <summary>Modal dialogs and pickers, owned by the main window. UI thread only.</summary>
public interface IDialogService
{
    /// <summary>Asks a yes/no question. Returns true when the user confirms.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool isDestructive = false);

    Task ShowMessageAsync(string title, string message);

    Task ShowErrorAsync(string title, string message, string? details = null);

    /// <summary>Shows the add/edit account dialog. Returns true when the account was saved.</summary>
    Task<bool> EditAccountAsync(AccountEditorViewModel editor);

    Task<LegacyImportChoice> ShowLegacyImportAsync(LegacyImportViewModel viewModel);

    /// <summary>Lets the user pick a folder. Returns its local path, or null when cancelled.</summary>
    Task<string?> PickFolderAsync(string title);
}
