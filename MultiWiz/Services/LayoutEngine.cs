using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace MultiWiz.Services;

/// <summary>
/// Layout presets for tiling windows
/// </summary>
public enum TilingLayout
{
    Single,              // One window fullscreen
    SideBySide,          // 2 windows split 50/50
    Grid2x2,             // 4 windows in 2x2 grid
    ThreeColumn,         // 3 windows horizontal
    PrimarySecondary,    // 1 large (70%) + 2 stacked small (30%)
    Sidebar              // All windows in vertical list
}

/// <summary>
/// Represents a calculated window tile with position and associated window
/// </summary>
public class WindowTile
{
    public IntPtr WindowHandle { get; set; }
    public Rect Bounds { get; set; }
    public int Index { get; set; }
    public bool IsFocused { get; set; }

    public WindowTile(IntPtr windowHandle, Rect bounds, int index, bool isFocused = false)
    {
        WindowHandle = windowHandle;
        Bounds = bounds;
        Index = index;
        IsFocused = isFocused;
    }
}

/// <summary>
/// Calculates window positions and sizes for various tiling layouts
/// </summary>
public class LayoutEngine
{
    private const double MinWindowWidth = 200;
    private const double MinWindowHeight = 150;
    private const double Padding = 8; // Space between windows

    /// <summary>
    /// Calculates window tiles for a given layout
    /// </summary>
    /// <param name="layout">The layout preset to use</param>
    /// <param name="containerSize">The size of the container area</param>
    /// <param name="windows">List of window handles to tile</param>
    /// <param name="focusedWindowIndex">Index of the focused window</param>
    /// <returns>List of window tiles with calculated positions</returns>
    public static List<WindowTile> CalculateLayout(TilingLayout layout, Size containerSize, List<IntPtr> windows, int focusedWindowIndex = 0)
    {
        if (windows == null || windows.Count == 0)
        {
            return new List<WindowTile>();
        }

        // Clamp focused index
        focusedWindowIndex = Math.Max(0, Math.Min(focusedWindowIndex, windows.Count - 1));

        return layout switch
        {
            TilingLayout.Single => CalculateSingleLayout(containerSize, windows, focusedWindowIndex),
            TilingLayout.SideBySide => CalculateSideBySideLayout(containerSize, windows, focusedWindowIndex),
            TilingLayout.Grid2x2 => CalculateGrid2x2Layout(containerSize, windows, focusedWindowIndex),
            TilingLayout.ThreeColumn => CalculateThreeColumnLayout(containerSize, windows, focusedWindowIndex),
            TilingLayout.PrimarySecondary => CalculatePrimarySecondaryLayout(containerSize, windows, focusedWindowIndex),
            TilingLayout.Sidebar => CalculateSidebarLayout(containerSize, windows, focusedWindowIndex),
            _ => CalculateSingleLayout(containerSize, windows, focusedWindowIndex)
        };
    }

    /// <summary>
    /// Single window fullscreen
    /// </summary>
    private static List<WindowTile> CalculateSingleLayout(Size containerSize, List<IntPtr> windows, int focusedWindowIndex)
    {
        var tiles = new List<WindowTile>();

        if (windows.Count == 0)
            return tiles;

        // Show only the focused window
        var window = windows[focusedWindowIndex];
        var bounds = new Rect(0, 0, containerSize.Width, containerSize.Height);
        tiles.Add(new WindowTile(window, bounds, focusedWindowIndex, true));

        return tiles;
    }

    /// <summary>
    /// Two windows split 50/50 vertically
    /// </summary>
    private static List<WindowTile> CalculateSideBySideLayout(Size containerSize, List<IntPtr> windows, int focusedWindowIndex)
    {
        var tiles = new List<WindowTile>();

        if (windows.Count == 0)
            return tiles;

        if (windows.Count == 1)
        {
            return CalculateSingleLayout(containerSize, windows, focusedWindowIndex);
        }

        // Split container in half
        double halfWidth = (containerSize.Width - Padding) / 2;

        for (int i = 0; i < Math.Min(2, windows.Count); i++)
        {
            double x = i == 0 ? 0 : halfWidth + Padding;
            var bounds = new Rect(x, 0, halfWidth, containerSize.Height);
            tiles.Add(new WindowTile(windows[i], bounds, i, i == focusedWindowIndex));
        }

        return tiles;
    }

    /// <summary>
    /// Four windows in a 2x2 grid
    /// </summary>
    private static List<WindowTile> CalculateGrid2x2Layout(Size containerSize, List<IntPtr> windows, int focusedWindowIndex)
    {
        var tiles = new List<WindowTile>();

        if (windows.Count == 0)
            return tiles;

        if (windows.Count == 1)
        {
            return CalculateSingleLayout(containerSize, windows, focusedWindowIndex);
        }

        if (windows.Count == 2)
        {
            return CalculateSideBySideLayout(containerSize, windows, focusedWindowIndex);
        }

        // Calculate grid dimensions
        double halfWidth = (containerSize.Width - Padding) / 2;
        double halfHeight = (containerSize.Height - Padding) / 2;

        for (int i = 0; i < Math.Min(4, windows.Count); i++)
        {
            int row = i / 2;
            int col = i % 2;

            double x = col * (halfWidth + Padding);
            double y = row * (halfHeight + Padding);

            var bounds = new Rect(x, y, halfWidth, halfHeight);
            tiles.Add(new WindowTile(windows[i], bounds, i, i == focusedWindowIndex));
        }

        return tiles;
    }

    /// <summary>
    /// Three windows in horizontal columns
    /// </summary>
    private static List<WindowTile> CalculateThreeColumnLayout(Size containerSize, List<IntPtr> windows, int focusedWindowIndex)
    {
        var tiles = new List<WindowTile>();

        if (windows.Count == 0)
            return tiles;

        if (windows.Count == 1)
        {
            return CalculateSingleLayout(containerSize, windows, focusedWindowIndex);
        }

        if (windows.Count == 2)
        {
            return CalculateSideBySideLayout(containerSize, windows, focusedWindowIndex);
        }

        // Calculate column width
        int numColumns = Math.Min(3, windows.Count);
        double totalPadding = Padding * (numColumns - 1);
        double columnWidth = (containerSize.Width - totalPadding) / numColumns;

        for (int i = 0; i < Math.Min(3, windows.Count); i++)
        {
            double x = i * (columnWidth + Padding);
            var bounds = new Rect(x, 0, columnWidth, containerSize.Height);
            tiles.Add(new WindowTile(windows[i], bounds, i, i == focusedWindowIndex));
        }

        return tiles;
    }

    /// <summary>
    /// One large window (70%) on left, two smaller stacked windows (30%) on right
    /// </summary>
    private static List<WindowTile> CalculatePrimarySecondaryLayout(Size containerSize, List<IntPtr> windows, int focusedWindowIndex)
    {
        var tiles = new List<WindowTile>();

        if (windows.Count == 0)
            return tiles;

        if (windows.Count == 1)
        {
            return CalculateSingleLayout(containerSize, windows, focusedWindowIndex);
        }

        if (windows.Count == 2)
        {
            return CalculateSideBySideLayout(containerSize, windows, focusedWindowIndex);
        }

        // Primary window takes 70% width
        double primaryWidth = containerSize.Width * 0.7 - Padding / 2;
        double secondaryWidth = containerSize.Width * 0.3 - Padding / 2;

        // First window - primary (left, full height)
        var primaryBounds = new Rect(0, 0, primaryWidth, containerSize.Height);
        tiles.Add(new WindowTile(windows[0], primaryBounds, 0, 0 == focusedWindowIndex));

        // Remaining windows stacked on right
        int secondaryCount = Math.Min(windows.Count - 1, 4); // Max 4 secondary windows
        double secondaryHeight = (containerSize.Height - Padding * (secondaryCount - 1)) / secondaryCount;

        for (int i = 1; i <= secondaryCount; i++)
        {
            double x = primaryWidth + Padding;
            double y = (i - 1) * (secondaryHeight + Padding);

            var bounds = new Rect(x, y, secondaryWidth, secondaryHeight);
            tiles.Add(new WindowTile(windows[i], bounds, i, i == focusedWindowIndex));
        }

        return tiles;
    }

    /// <summary>
    /// All windows in a vertical scrollable list
    /// </summary>
    private static List<WindowTile> CalculateSidebarLayout(Size containerSize, List<IntPtr> windows, int focusedWindowIndex)
    {
        var tiles = new List<WindowTile>();

        if (windows.Count == 0)
            return tiles;

        // Each window gets equal height (or minimum height)
        double idealHeight = Math.Max(MinWindowHeight, containerSize.Height / Math.Min(windows.Count, 5));

        for (int i = 0; i < windows.Count; i++)
        {
            double y = i * (idealHeight + Padding);
            var bounds = new Rect(0, y, containerSize.Width, idealHeight);
            tiles.Add(new WindowTile(windows[i], bounds, i, i == focusedWindowIndex));
        }

        return tiles;
    }

    /// <summary>
    /// Gets a human-readable name for a layout
    /// </summary>
    public static string GetLayoutName(TilingLayout layout)
    {
        return layout switch
        {
            TilingLayout.Single => "Single Window",
            TilingLayout.SideBySide => "Side by Side",
            TilingLayout.Grid2x2 => "2×2 Grid",
            TilingLayout.ThreeColumn => "3 Columns",
            TilingLayout.PrimarySecondary => "Primary + Secondary",
            TilingLayout.Sidebar => "Sidebar",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// Gets the recommended layout for a given window count
    /// </summary>
    public static TilingLayout GetRecommendedLayout(int windowCount)
    {
        return windowCount switch
        {
            0 or 1 => TilingLayout.Single,
            2 => TilingLayout.SideBySide,
            3 => TilingLayout.ThreeColumn,
            4 => TilingLayout.Grid2x2,
            _ => TilingLayout.PrimarySecondary
        };
    }
}
