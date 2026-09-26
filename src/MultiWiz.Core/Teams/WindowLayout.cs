namespace MultiWiz.Core.Teams;

/// <summary>
/// One window placement, as fractions (0..1) of a monitor's work area.
/// <see cref="MonitorIndex"/> refers to <see cref="Platform.MonitorInfo.Index"/>; indexes beyond the
/// number of connected monitors wrap around (modulo).
/// </summary>
public sealed record LayoutCell(int MonitorIndex, double X, double Y, double Width, double Height);

/// <summary>An ordered set of cells. Slot N of a team goes to cell N (cells repeat if there are more windows than cells).</summary>
public sealed record WindowLayout
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<LayoutCell> Cells { get; init; } = [];
    public bool IsBuiltIn { get; init; }
}
