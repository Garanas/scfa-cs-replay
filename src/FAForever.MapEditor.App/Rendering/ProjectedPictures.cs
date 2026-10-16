using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;
using Microsoft.Extensions.Logging;

namespace FAForever.MapEditor.App.Rendering;

/// <summary>
/// The pictures of the projected images (user story N-2) decoded for drawing: each once, kept as
/// long as its image holds the picture, never decoded per frame. A picture is found by the bytes the
/// document holds (an edit that does not replace the picture keeps them), so moving, scaling and
/// turning an image decodes nothing. Greyscale is a picture of its own, made once.
/// </summary>
internal sealed class ProjectedPictures(Func<ILogger> logger)
{
    private sealed class Held
    {
        public Bitmap? Colour;
        public Bitmap? Grey;
        public bool Failed;
    }

    private readonly ConditionalWeakTable<byte[], Held> _held = [];

    /// <summary>
    /// The bitmap to draw for an image; null when its picture does not decode (logged once).
    /// </summary>
    public Bitmap? Of(ProjectedImage image)
    {
        byte[]? bytes = ImmutableCollectionsMarshal.AsArray(image.Picture);
        if (bytes is null)
        {
            return null;
        }
        Held held = _held.GetValue(bytes, _ => new Held());
        if (held.Failed)
        {
            return null;
        }
        try
        {
            if (image.Greyscale)
            {
                return held.Grey ??= Decode(ProjectedImages.Greyscale(bytes));
            }
            return held.Colour ??= Decode(bytes);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            held.Failed = true;
            logger().LogError(exception, "The picture of the projected image {Image} cannot be drawn: {Reason}", image.Name, exception.Message);
            return null;
        }
    }

    private static Bitmap Decode(byte[] png)
    {
        using Performance.Span measure = Performance.Measure("top.images-decode");
        using MemoryStream stream = new MemoryStream(png, writable: false);
        return new Bitmap(stream);
    }
}
