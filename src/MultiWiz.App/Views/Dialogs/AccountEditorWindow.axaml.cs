using Avalonia.Controls;
using Avalonia.Input;
using MultiWiz.App.ViewModels;

namespace MultiWiz.App.Views.Dialogs;

/// <summary>Add/edit account dialog. Closes with true after a successful save.</summary>
public partial class AccountEditorWindow : Window
{
    private AccountEditorViewModel? _viewModel;

    public AccountEditorWindow()
    {
        InitializeComponent();
        Opened += (_, _) => this.FindControl<TextBox>("NameBox")?.Focus();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
        }

        _viewModel = DataContext as AccountEditorViewModel;
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
            _viewModel.Password = string.Empty;
        }

        base.OnClosed(e);
    }

    private void OnCloseRequested(object? sender, bool saved) => Close(saved);
}
