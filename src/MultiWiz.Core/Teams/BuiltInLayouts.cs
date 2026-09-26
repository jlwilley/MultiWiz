namespace MultiWiz.Core.Teams;

public static class BuiltInLayouts
{
    public const string NoneId = "none";

    public static readonly WindowLayout None = new() { Id = NoneId, Name = "Don't move windows", IsBuiltIn = true };

    public static readonly WindowLayout SideBySide = new()
    {
        Id = "side-by-side", Name = "Side by side", IsBuiltIn = true,
        Cells = [new(0, 0, 0, 0.5, 1), new(0, 0.5, 0, 0.5, 1)],
    };

    public static readonly WindowLayout Grid2x2 = new()
    {
        Id = "grid-2x2", Name = "2 × 2 grid", IsBuiltIn = true,
        Cells = [new(0, 0, 0, 0.5, 0.5), new(0, 0.5, 0, 0.5, 0.5), new(0, 0, 0.5, 0.5, 0.5), new(0, 0.5, 0.5, 0.5, 0.5)],
    };

    public static readonly WindowLayout Grid3x2 = new()
    {
        Id = "grid-3x2", Name = "3 × 2 grid", IsBuiltIn = true,
        Cells =
        [
            new(0, 0, 0, 1 / 3d, 0.5), new(0, 1 / 3d, 0, 1 / 3d, 0.5), new(0, 2 / 3d, 0, 1 / 3d, 0.5),
            new(0, 0, 0.5, 1 / 3d, 0.5), new(0, 1 / 3d, 0.5, 1 / 3d, 0.5), new(0, 2 / 3d, 0.5, 1 / 3d, 0.5),
        ],
    };

    public static readonly WindowLayout MainPlusThree = new()
    {
        Id = "main-plus-3", Name = "Main + 3", IsBuiltIn = true,
        Cells =
        [
            new(0, 0, 0, 2 / 3d, 1),
            new(0, 2 / 3d, 0, 1 / 3d, 1 / 3d), new(0, 2 / 3d, 1 / 3d, 1 / 3d, 1 / 3d), new(0, 2 / 3d, 2 / 3d, 1 / 3d, 1 / 3d),
        ],
    };

    public static readonly WindowLayout OnePerMonitor = new()
    {
        Id = "one-per-monitor", Name = "One per monitor", IsBuiltIn = true,
        Cells = [new(0, 0, 0, 1, 1), new(1, 0, 0, 1, 1), new(2, 0, 0, 1, 1), new(3, 0, 0, 1, 1)],
    };

    public static IReadOnlyList<WindowLayout> All { get; } = [None, SideBySide, Grid2x2, Grid3x2, MainPlusThree, OnePerMonitor];

    public static WindowLayout? Find(string id) => All.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));
}
