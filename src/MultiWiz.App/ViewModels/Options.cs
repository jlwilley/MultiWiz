using MultiWiz.Core.Games;
using MultiWiz.Core.Settings;
using MultiWiz.Core.Teams;

namespace MultiWiz.App.ViewModels;

// Small immutable choices shown in combo boxes and pickers. ToString() is what a ComboBox displays.

public sealed record GameOption(GameKind Game, string Label)
{
    public static IReadOnlyList<GameOption> All { get; } =
    [
        new(GameKind.Wizard101, "Wizard101"),
        new(GameKind.Pirate101, "Pirate101"),
    ];

    public static GameOption For(GameKind game) => All.FirstOrDefault(option => option.Game == game) ?? All[0];

    public override string ToString() => Label;
}

public sealed record RealmOption(string RealmId, string Label)
{
    public override string ToString() => Label;
}

/// <summary>An install choice; <see cref="InstallId"/> null means "Automatic" (the preferred install for the game).</summary>
public sealed record InstallOption(string? InstallId, string Label)
{
    public static InstallOption Automatic { get; } = new(null, "Automatic");

    public static InstallOption From(GameInstall install) => new(install.Id, InstallLabels.Describe(install));

    public override string ToString() => Label;
}

public sealed record AccountOption(Guid AccountId, string Label)
{
    public override string ToString() => Label;
}

public sealed record ThemeOption(ThemePreference Value, string Label)
{
    public static IReadOnlyList<ThemeOption> All { get; } =
    [
        new(ThemePreference.System, "Use Windows setting"),
        new(ThemePreference.Dark, "Dark (Catppuccin Mocha)"),
        new(ThemePreference.Light, "Light (Catppuccin Latte)"),
    ];

    public override string ToString() => Label;
}

public sealed record SwitcherViewOption(SwitcherViewMode Value, string Label)
{
    public static IReadOnlyList<SwitcherViewOption> All { get; } =
    [
        new(SwitcherViewMode.List, "List (names only)"),
        new(SwitcherViewMode.Previews, "Small previews"),
        new(SwitcherViewMode.Large, "Large previews"),
    ];

    public override string ToString() => Label;
}

public sealed record ChannelOption(UpdateChannel Value, string Label)
{
    public static IReadOnlyList<ChannelOption> All { get; } =
    [
        new(UpdateChannel.Stable, "Stable"),
        new(UpdateChannel.Beta, "Beta (early builds)"),
    ];

    public override string ToString() => Label;
}

/// <summary>An account accent colour; <see cref="Color"/> null means no colour.</summary>
public sealed record AccentSwatch(string? Color, string Name)
{
    public static IReadOnlyList<AccentSwatch> Palette { get; } =
    [
        new(null, "No colour"),
        new("#CBA6F7", "Mauve"),
        new("#89B4FA", "Blue"),
        new("#74C7EC", "Sapphire"),
        new("#94E2D5", "Teal"),
        new("#A6E3A1", "Green"),
        new("#F9E2AF", "Yellow"),
        new("#FAB387", "Peach"),
        new("#F38BA8", "Red"),
        new("#F5C2E7", "Pink"),
        new("#B4BEFE", "Lavender"),
    ];
}

public sealed record LayoutOption(WindowLayout Layout)
{
    public string Name => Layout.Name;

    public string Description => Layout.Id switch
    {
        BuiltInLayouts.NoneId => "Windows stay where the game opens them",
        "one-per-monitor" => "One window fills each monitor",
        _ => Layout.Cells.Count == 1 ? "1 window" : $"{Layout.Cells.Count} windows per screen",
    };
}

internal static class InstallLabels
{
    public static string Describe(GameInstall install)
    {
        var name = string.IsNullOrWhiteSpace(install.DisplayName) ? install.Game.ToString() : install.DisplayName;
        return $"{name} · {SourceName(install.Source)}";
    }

    public static string SourceName(InstallSource source) => source switch
    {
        InstallSource.Steam => "Steam",
        InstallSource.Custom => "Custom folder",
        _ => "KingsIsle installer",
    };
}
