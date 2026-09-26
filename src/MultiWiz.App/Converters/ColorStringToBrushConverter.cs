using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MultiWiz.App.Converters;

/// <summary>
/// Converts an "#RRGGBB" (or any Avalonia colour string) into a brush. Empty or invalid values use
/// <see cref="Fallback"/>, so an account without an accent colour still gets a neutral dot.
/// </summary>
public sealed class ColorStringToBrushConverter : IValueConverter
{
    private static readonly IBrush NeutralBrush = new SolidColorBrush(Color.FromRgb(0x93, 0x99, 0xB2));

    public IBrush Fallback { get; init; } = NeutralBrush;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && Color.TryParse(text, out var color) ? new SolidColorBrush(color) : Fallback;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
