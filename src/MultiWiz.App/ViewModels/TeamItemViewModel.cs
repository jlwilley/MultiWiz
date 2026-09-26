using CommunityToolkit.Mvvm.ComponentModel;

namespace MultiWiz.App.ViewModels;

/// <summary>A team in the Teams page list.</summary>
public sealed partial class TeamItemViewModel : ObservableObject
{
    public TeamItemViewModel(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>True when the switcher currently follows this team's slot order.</summary>
    [ObservableProperty]
    public partial bool IsActive { get; set; }
}
