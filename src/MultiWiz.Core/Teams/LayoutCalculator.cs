using MultiWiz.Core.Platform;
using MultiWiz.Core.Primitives;

namespace MultiWiz.Core.Teams;

/// <summary>Turns a <see cref="WindowLayout"/> into window rectangles for the connected monitors.</summary>
public static class LayoutCalculator
{
    // Fractions are snapped to this many decimals before scaling, so that one cell's far edge (X + Width) and the
    // neighbouring cell's near edge (X) always round to the same pixel even when the sums differ in the last bit.
    private const int FractionDecimals = 6;

    /// <summary>
    /// One rectangle per window, in slot order. Empty for a layout without cells (<see cref="BuiltInLayouts.None"/>),
    /// when there are no monitors, or when <paramref name="windowCount"/> is not positive. Cells repeat when there are
    /// more windows than cells, <see cref="LayoutCell.MonitorIndex"/> wraps modulo the monitor count, and rectangles
    /// are computed from each monitor's work area.
    /// </summary>
    public static IReadOnlyList<PixelRect> Arrange(WindowLayout layout, IReadOnlyList<MonitorInfo> monitors, int windowCount)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(monitors);

        if (windowCount <= 0 || monitors.Count == 0 || layout.Cells.Count == 0)
        {
            return [];
        }

        var ordered = monitors.OrderBy(monitor => monitor.Index).ToArray();
        var rects = new PixelRect[windowCount];
        for (var i = 0; i < windowCount; i++)
        {
            var cell = layout.Cells[i % layout.Cells.Count];
            var monitor = ordered[PositiveModulo(cell.MonitorIndex, ordered.Length)];
            rects[i] = Place(cell, monitor.WorkArea);
        }

        return rects;
    }

    private static PixelRect Place(LayoutCell cell, PixelRect area)
    {
        var left = Edge(area.X, area.Width, cell.X);
        var right = Edge(area.X, area.Width, cell.X + cell.Width);
        var top = Edge(area.Y, area.Height, cell.Y);
        var bottom = Edge(area.Y, area.Height, cell.Y + cell.Height);
        return new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static int Edge(int origin, int length, double fraction)
    {
        var clamped = double.IsFinite(fraction) ? Math.Clamp(fraction, 0d, 1d) : 0d;
        var snapped = Math.Round(clamped, FractionDecimals, MidpointRounding.AwayFromZero);
        return origin + (int)Math.Round(length * snapped, MidpointRounding.AwayFromZero);
    }

    private static int PositiveModulo(int value, int modulus) => ((value % modulus) + modulus) % modulus;
}
