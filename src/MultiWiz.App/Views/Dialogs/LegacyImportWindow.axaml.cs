using Avalonia.Controls;
using Avalonia.Input;
using MultiWiz.App.ViewModels;

namespace MultiWiz.App.Views.Dialogs;

/// <summary>The MultiWiz 3 import prompt. Closes with the user's <see cref="LegacyImportChoice"/> (Later when dismissed).</summary>
public partial class LegacyImportWindow : Window
{
    private LegacyImportViewModel? _viewModel;

    public LegacyImportWindow()
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

        _viewModel = DataContext as LegacyImportViewModel;
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
            Close(LegacyImportChoice.Later);
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

    private void OnCloseRequested(object? sender, LegacyImportChoice choice) => Close(choice);
}
