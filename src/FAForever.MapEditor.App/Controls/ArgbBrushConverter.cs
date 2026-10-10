using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// A brush of a colour that comes from the map's data as <c>0xAARRGGBB</c>, such as a terrain
/// type's (the legend of the terrain types tool): the view models hold numbers, the views draw.
/// </summary>
public sealed class ArgbBrushConverter : IValueConverter
{
    public static ArgbBrushConverter Instance { get; } = new ArgbBrushConverter();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is uint argb ? new SolidColorBrush(Color.FromUInt32(argb)) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
