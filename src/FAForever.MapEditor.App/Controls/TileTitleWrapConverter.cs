using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// How a tile's name wraps and is cut (<c>TextBlock.tiletitle</c>, bound to both
/// <c>TextWrapping</c> and <c>TextTrimming</c>): between its words when it has several ("Aeon
/// Administrative Structure", cut at its end); a name of one word (<c>des_boulder01</c>) stays on
/// one line, cut in its middle (<c>des_bou…lder01</c>) so the ends that tell variants apart show,
/// since the text layout would break a word wider than the tile in two.
/// </summary>
public sealed class TileTitleWrapConverter : IValueConverter
{
    public static TileTitleWrapConverter Instance { get; } = new TileTitleWrapConverter();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool words = value is string text && text.Trim().Contains(' ');
        return targetType == typeof(TextTrimming)
            ? words ? TextTrimming.CharacterEllipsis : TextTrimming.PrefixCharacterEllipsis
            : words ? TextWrapping.Wrap : TextWrapping.NoWrap;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
