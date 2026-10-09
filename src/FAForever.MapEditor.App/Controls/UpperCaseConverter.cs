using System.Globalization;
using Avalonia.Data.Converters;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// A text in capitals, for the label of a group in a panel (<c>TextBlock.overline</c>): a
/// <c>TextBlock</c> has no text transform, so the views write
/// <c>Text="{Binding Title, Converter={x:Static controls:UpperCaseConverter.Instance}}"</c>.
/// </summary>
public sealed class UpperCaseConverter : IValueConverter
{
    public static UpperCaseConverter Instance { get; } = new UpperCaseConverter();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text ? text.ToUpper(culture) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
