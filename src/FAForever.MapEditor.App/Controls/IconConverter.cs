using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The icon of a key in <c>Styles/Icons.axaml</c>, e.g. <c>Icon.Terrain</c>: the view models name
/// icons by key and leave drawing to the views.
/// </summary>
public sealed class IconConverter : IValueConverter
{
    public static IconConverter Instance { get; } = new IconConverter();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && Application.Current is Application application
            && application.TryGetResource(key, application.ActualThemeVariant, out object? icon) && icon is Geometry geometry ? geometry : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
