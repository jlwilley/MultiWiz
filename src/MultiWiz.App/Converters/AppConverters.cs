using Avalonia.Data.Converters;

namespace MultiWiz.App.Converters;

/// <summary>Shared converter instances for XAML: <c>Converter={x:Static conv:AppConverters.AccentBrush}</c>.</summary>
public static class AppConverters
{
    /// <summary>Accent colour string to brush (neutral grey when unset).</summary>
    public static IValueConverter AccentBrush { get; } = new ColorStringToBrushConverter();

    /// <summary>A 0..1 fraction as a whole percentage, e.g. 0.95 → "95%".</summary>
    public static IValueConverter Percent { get; } = new FuncValueConverter<double, string>(value => $"{value * 100:0}%");

    /// <summary>A 0..100 value as a percentage, e.g. 40 → "40%".</summary>
    public static IValueConverter WholePercent { get; } = new FuncValueConverter<double, string>(value => $"{value:0}%");
}
