using Avalonia.Controls;
using Avalonia.Input;
using MultiWiz.App.ViewModels;

namespace MultiWiz.App.Views.Dialogs;

/// <summary>Message, confirmation and error dialog. Closes with true (confirmed) or false (cancelled, Esc, or X).</summary>
public partial class MessageDialog : Window
{
    private MessageDialogViewModel? _viewModel;

    public MessageDialog()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
        }

        _viewModel = DataContext as MessageDialogViewModel;
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested += OnCloseRequested;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(false);
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
        }

        base.OnClosed(e);
    }

    private void OnCloseRequested(object? sender, bool confirmed) => Close(confirmed);
}
