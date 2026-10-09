using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// An enum as the index of a tab, both ways, for a <c>TabControl</c> whose tabs are in the order
/// of the enum's values: <c>SelectedIndex="{Binding Dock, Converter={x:Static controls:EnumIndexConverter.Instance}}"</c>.
/// </summary>
public sealed class EnumIndexConverter : IValueConverter
{
    public static EnumIndexConverter Instance { get; } = new EnumIndexConverter();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum choice ? System.Convert.ToInt32(choice, culture) : -1;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index && index >= 0 && targetType.IsEnum
            ? Enum.ToObject(targetType, index)
            : BindingOperations.DoNothing;
}
