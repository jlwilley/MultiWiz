using CommunityToolkit.Mvvm.ComponentModel;

namespace MultiWiz.App.Overlays;

/// <summary>What a name badge shows: the client's switcher slot, account name and accent colour.</summary>
public sealed partial class NameBadgeViewModel : ObservableObject
{
    [ObservableProperty]
    public partial int Slot { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? AccentColor { get; set; }
}
