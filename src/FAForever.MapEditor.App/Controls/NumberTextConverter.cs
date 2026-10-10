using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using FAForever.MapEditor.App.ViewModels;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// A text field bound straight to a number (<c>double</c>, <c>float</c>, <c>int</c>) as the editor
/// writes and reads numbers (<see cref="EditorCulture"/>): shown with a point, read with a point or
/// a comma, whatever the computer's language. The views write
/// <c>Text="{Binding MinHeight, Converter={x:Static controls:NumberTextConverter.Instance}}"</c>;
/// a text that is not a number (or not a whole one for an <c>int</c>) leaves the value as it was.
/// </summary>
public sealed class NumberTextConverter : IValueConverter
{
    public static NumberTextConverter Instance { get; } = new NumberTextConverter();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        double number => EditorCulture.Format(number),
        float number => EditorCulture.Format(number),
        int number => number.ToString(CultureInfo.InvariantCulture),
        _ => value,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Type type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (value is not string text || !EditorCulture.TryParse(text, out double number))
        {
            return new BindingNotification(new FormatException($"'{value}' is not a number: write 12.5"), BindingErrorType.DataValidationError);
        }
        if (type == typeof(int))
        {
            return number == Math.Round(number) && number is >= int.MinValue and <= int.MaxValue
                ? (int)number
                : new BindingNotification(new FormatException($"'{value}' is not a whole number"), BindingErrorType.DataValidationError);
        }
        return type == typeof(float) ? (object)(float)number : number;
    }
}
