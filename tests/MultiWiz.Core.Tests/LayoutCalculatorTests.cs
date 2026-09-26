using MultiWiz.Core.Platform;
using MultiWiz.Core.Primitives;
using MultiWiz.Core.Teams;
using MultiWiz.Core.Tests.Fakes;

namespace MultiWiz.Core.Tests;

public sealed class LayoutCalculatorTests
{
    // 1920x1080 with a 40 px taskbar: the work area is 1920x1040.
    private static readonly MonitorInfo Primary = FakeDisplayService.Monitor(0, 0, 0, 1920, 1080);
    private static readonly MonitorInfo Secondary = FakeDisplayService.Monitor(1, 1920, -200, 2560, 1440, taskbarHeight: 0);

    public static TheoryData<string> BuiltInLayoutIds()
    {
        var data = new TheoryData<string>();
        foreach (var layout in BuiltInLayouts.All)
        {
            data.Add(layout.Id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BuiltInLayoutIds))]
    public void Every_built_in_layout_places_windows_inside_the_work_area(string layoutId)
    {
        var layout = BuiltInLayouts.Find(layoutId)!;

        var rects = LayoutCalculator.Arrange(layout, [Primary], 6);

        if (layout.Cells.Count == 0)
        {
            Assert.Empty(rects);
            return;
        }

        Assert.Equal(6, rects.Count);
        Assert.All(rects, rect =>
        {
            Assert.False(rect.IsEmpty);
            Assert.InRange(rect.X, Primary.WorkArea.X, Primary.WorkArea.Right);
            Assert.InRange(rect.Right, Primary.WorkArea.X, Primary.WorkArea.Right);
            Assert.InRange(rect.Y, Primary.WorkArea.Y, Primary.WorkArea.Bottom);
            Assert.InRange(rect.Bottom, Primary.WorkArea.Y, Primary.WorkArea.Bottom);
        });
    }

    [Fact]
    public void None_layout_and_missing_monitors_produce_nothing()
    {
        Assert.Empty(LayoutCalculator.Arrange(BuiltInLayouts.None, [Primary], 4));
        Assert.Empty(LayoutCalculator.Arrange(BuiltInLayouts.Grid2x2, [], 4));
        Assert.Empty(LayoutCalculator.Arrange(BuiltInLayouts.Grid2x2, [Primary], 0));
    }

    [Fact]
    public void Side_by_side_splits_the_work_area_in_half()
    {
        var rects = LayoutCalculator.Arrange(BuiltInLayouts.SideBySide, [Primary], 2);

        Assert.Equal(new[] { new PixelRect(0, 0, 960, 1040), new PixelRect(960, 0, 960, 1040) }, rects.ToArray());
    }

    [Fact]
    public void Grid_2x2_uses_the_monitor_offset_and_work_area()
    {
        var rects = LayoutCalculator.Arrange(BuiltInLayouts.Grid2x2, [Secondary], 4);

        Assert.Equal(
            new[]
            {
                new PixelRect(1920, -200, 1280, 720), new PixelRect(3200, -200, 1280, 720),
                new PixelRect(1920, 520, 1280, 720), new PixelRect(3200, 520, 1280, 720),
            },
            rects.ToArray());
    }

    [Fact]
    public void Main_plus_three_gives_the_first_slot_two_thirds()
    {
        var monitor = FakeDisplayService.Monitor(0, 0, 0, 1800, 900, taskbarHeight: 0);

        var rects = LayoutCalculator.Arrange(BuiltInLayouts.MainPlusThree, [monitor], 4);

        Assert.Equal(
            new[]
            {
                new PixelRect(0, 0, 1200, 900),
                new PixelRect(1200, 0, 600, 300), new PixelRect(1200, 300, 600, 300), new PixelRect(1200, 600, 600, 300),
            },
            rects.ToArray());
    }

    [Theory]
    [InlineData(1000, 701)]
    [InlineData(1366, 728)]
    [InlineData(2561, 1439)]
    [InlineData(7, 5)]
    public void Neighbouring_cells_share_edges_exactly(int width, int height)
    {
        var monitor = FakeDisplayService.Monitor(0, 100, 50, width, height, taskbarHeight: 0);

        var rects = LayoutCalculator.Arrange(BuiltInLayouts.Grid3x2, [monitor], 6);

        // Row by row: each cell starts where the previous one ends, and the last one ends at the work area's edge.
        for (var row = 0; row < 2; row++)
        {
            Assert.Equal(monitor.WorkArea.X, rects[row * 3].X);
            Assert.Equal(rects[row * 3].Right, rects[(row * 3) + 1].X);
            Assert.Equal(rects[(row * 3) + 1].Right, rects[(row * 3) + 2].X);
            Assert.Equal(monitor.WorkArea.Right, rects[(row * 3) + 2].Right);
        }

        for (var column = 0; column < 3; column++)
        {
            Assert.Equal(monitor.WorkArea.Y, rects[column].Y);
            Assert.Equal(rects[column].Bottom, rects[column + 3].Y);
            Assert.Equal(monitor.WorkArea.Bottom, rects[column + 3].Bottom);
        }
    }

    [Fact]
    public void Thirds_round_to_whole_pixels()
    {
        var monitor = FakeDisplayService.Monitor(0, 0, 0, 1000, 600, taskbarHeight: 0);

        var rects = LayoutCalculator.Arrange(BuiltInLayouts.Grid3x2, [monitor], 3);

        Assert.Equal(new[] { 0, 333, 667 }, rects.Select(rect => rect.X).ToArray());
        Assert.Equal(new[] { 333, 334, 333 }, rects.Select(rect => rect.Width).ToArray());
    }

    [Fact]
    public void Cells_repeat_when_there_are_more_windows_than_cells()
    {
        var rects = LayoutCalculator.Arrange(BuiltInLayouts.SideBySide, [Primary], 5);

        Assert.Equal(5, rects.Count);
        Assert.Equal(rects[0], rects[2]);
        Assert.Equal(rects[0], rects[4]);
        Assert.Equal(rects[1], rects[3]);
    }

    [Fact]
    public void Monitor_indexes_wrap_around_the_connected_monitors()
    {
        var rects = LayoutCalculator.Arrange(BuiltInLayouts.OnePerMonitor, [Primary, Secondary], 4);

        Assert.Equal(Primary.WorkArea, rects[0]);
        Assert.Equal(Secondary.WorkArea, rects[1]);
        Assert.Equal(Primary.WorkArea, rects[2]);
        Assert.Equal(Secondary.WorkArea, rects[3]);
    }

    [Fact]
    public void Monitors_are_matched_by_index_not_list_position()
    {
        var rects = LayoutCalculator.Arrange(BuiltInLayouts.OnePerMonitor, [Secondary, Primary], 2);

        Assert.Equal(Primary.WorkArea, rects[0]);
        Assert.Equal(Secondary.WorkArea, rects[1]);
    }

    [Fact]
    public void Out_of_range_fractions_are_clamped_to_the_work_area()
    {
        var layout = new WindowLayout
        {
            Id = "custom",
            Name = "Custom",
            Cells = [new LayoutCell(-1, -0.5, 0.5, 2, double.NaN)],
        };
        var monitor = FakeDisplayService.Monitor(0, 0, 0, 1000, 500, taskbarHeight: 0);

        var rect = Assert.Single(LayoutCalculator.Arrange(layout, [monitor], 1));

        Assert.Equal(new PixelRect(0, 250, 1000, 0), rect);
    }
}
