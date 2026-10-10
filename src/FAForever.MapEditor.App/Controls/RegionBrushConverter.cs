using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The brush of a navigation mesh region's colour in the overlay's palette (user story T-8), from
/// its index from 0: the theme's token <c>MapRegion1Brush</c> and on.
/// </summary>
public sealed class RegionBrushConverter : IValueConverter
{
    public static RegionBrushConverter Instance { get; } = new RegionBrushConverter();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index && Application.Current is Application application
            && application.TryGetResource($"MapRegion{index + 1}Brush", application.ActualThemeVariant, out object? brush) && brush is IBrush found
            ? found
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
