using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// A colour of the map, written <c>#rrggbb</c> (<see cref="ViewModels.ColorSetting.Swatch"/>), as a
/// brush for its swatch: the colour comes from the map, so it is made in code.
/// </summary>
public sealed class SwatchConverter : IValueConverter
{
    public static SwatchConverter Instance { get; } = new SwatchConverter();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && Color.TryParse(text, out Color color) ? new SolidColorBrush(color) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// The preview the <c>.scmap</c> starts with (256 by 256 pixels) as a picture (user story M-1).
/// </summary>
public sealed class MapPreviewConverter : IValueConverter
{
    public static MapPreviewConverter Instance { get; } = new MapPreviewConverter();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TiledBuffer<Color32> image || image.Width <= 0 || image.Height <= 0)
        {
            return null;
        }
        Color32[] pixels = image.ToArray();
        WriteableBitmap bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using ILockedFramebuffer frame = bitmap.Lock();
        byte[] row = new byte[image.Width * 4];
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Color32 pixel = pixels[y * image.Width + x];
                row[x * 4] = pixel.Blue;
                row[x * 4 + 1] = pixel.Green;
                row[x * 4 + 2] = pixel.Red;
                // most previews have no alpha worth showing: an empty one would be invisible
                row[x * 4 + 3] = 255;
            }
            System.Runtime.InteropServices.Marshal.Copy(row, 0, frame.Address + y * frame.RowBytes, row.Length);
        }
        return bitmap;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
