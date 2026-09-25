namespace MultiWiz.Core.Teams;

/// <summary>A named group of accounts launched and arranged together. Order of <see cref="AccountIds"/> = slot order.</summary>
public sealed record Team
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<Guid> AccountIds { get; init; } = [];

    /// <summary>Id of a <see cref="WindowLayout"/>; <see cref="BuiltInLayouts.NoneId"/> leaves windows where the game puts them.</summary>
    public string LayoutId { get; init; } = BuiltInLayouts.NoneId;

    /// <summary>When false, windows are only moved, never resized.</summary>
    public bool ResizeWindows { get; init; } = true;

    public int SortOrder { get; init; }
}
