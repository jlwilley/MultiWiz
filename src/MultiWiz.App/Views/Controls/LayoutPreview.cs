using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MultiWiz.Core.Teams;

namespace MultiWiz.App.Views.Controls;

/// <summary>
/// A small drawing of a <see cref="WindowLayout"/>: one 16:9 screen per monitor used by the layout, with each
/// cell drawn as a rounded rectangle in its place.
/// </summary>
public sealed class LayoutPreview : Control
{
    public static readonly StyledProperty<WindowLayout?> LayoutProperty =
        AvaloniaProperty.Register<LayoutPreview, WindowLayout?>(nameof(Layout));

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<LayoutPreview, IBrush?>(nameof(Fill));

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<LayoutPreview, IBrush?>(nameof(Stroke));

    public static readonly StyledProperty<IBrush?> ScreenBrushProperty =
        AvaloniaProperty.Register<LayoutPreview, IBrush?>(nameof(ScreenBrush));

    private const double ScreenAspect = 16d / 9d;
    private const double ScreenGap = 6;
    private const double CellInset = 2;

    static LayoutPreview()
    {
        AffectsRender<LayoutPreview>(LayoutProperty, FillProperty, StrokeProperty, ScreenBrushProperty);
    }

    public WindowLayout? Layout
    {
        get => GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? ScreenBrush
    {
        get => GetValue(ScreenBrushProperty);
        set => SetValue(ScreenBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var cells = Layout?.Cells ?? [];
        var monitorCount = cells.Count == 0 ? 1 : cells.Max(cell => Math.Max(0, cell.MonitorIndex)) + 1;

        // Fit monitorCount screens side by side, each 16:9, centred in the control.
        var screenWidth = Math.Min(
            (bounds.Width - ScreenGap * (monitorCount - 1)) / monitorCount,
            bounds.Height * ScreenAspect);
        var screenHeight = screenWidth / ScreenAspect;
        var totalWidth = screenWidth * monitorCount + ScreenGap * (monitorCount - 1);
        var left = (bounds.Width - totalWidth) / 2;
        var top = (bounds.Height - screenHeight) / 2;

        var cellPen = Stroke is { } stroke ? new Pen(stroke, 1) : null;
        for (var monitor = 0; monitor < monitorCount; monitor++)
        {
            var screen = new Rect(left + monitor * (screenWidth + ScreenGap), top, screenWidth, screenHeight);
            context.DrawRectangle(ScreenBrush, null, screen, 4, 4);

            foreach (var cell in cells.Where(cell => cell.MonitorIndex == monitor))
            {
                var cellRect = new Rect(
                    screen.X + cell.X * screen.Width + CellInset,
                    screen.Y + cell.Y * screen.Height + CellInset,
                    Math.Max(0, cell.Width * screen.Width - CellInset * 2),
                    Math.Max(0, cell.Height * screen.Height - CellInset * 2));
                context.DrawRectangle(Fill, cellPen, cellRect, 3, 3);
            }
        }
    }
}
