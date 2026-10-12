using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using FAForever.MapEditor.App.Controls;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Rendering.Gpu;

/// <summary>
/// A label's text drawn into a picture (user story N-3): premultiplied red, green, blue and alpha, row by row from the top, with its
/// halo and a margin round it.
/// </summary>
public sealed record LabelBitmap(int Width, int Height, byte[] Rgba);

/// <summary>
/// The sketch's labels as pictures for the 3D view, drawn by the same <see cref="SketchLabelPainter"/> as the top view draws
/// them with, so that they look alike. A picture is drawn on the interface's thread, never inside a frame of the
/// 3D view (drawing there could disturb the card's state): <see cref="Get"/> answers null for one that is not made yet, asks for
/// it to be made and the view to draw again.
/// </summary>
public sealed class LabelBitmaps(SketchLabelPainter painter, Action drawAgain, ILogger? log = null)
{
    // room round the text for its halo, in pixels
    private const int Margin = 3;

    // pictures kept; past this the cache is emptied (a new generation) and made again as they are asked for
    private const int Capacity = 256;

    private Dictionary<(string Text, SketchColour Colour, int Pixels), LabelBitmap> _made = [];
    private readonly HashSet<(string Text, SketchColour Colour, int Pixels)> _queued = [];

    /// <summary>
    /// Counts the times the cache was emptied: whoever holds something made from a picture lets it go when this changes.
    /// </summary>
    public int Generation { get; private set; }

    /// <summary>
    /// How many pictures wait to be made.
    /// </summary>
    public int Pending => _queued.Count;

    /// <summary>
    /// The picture of a label's text at a size in pixels, or null while it is being made.
    /// </summary>
    public LabelBitmap? Get(SketchLabel label, int pixels)
    {
        (string, SketchColour, int) key = (label.Text, label.Colour, pixels);
        if (_made.TryGetValue(key, out LabelBitmap? bitmap))
        {
            return bitmap;
        }
        if (_queued.Add(key))
        {
            Dispatcher.UIThread.Post(() =>
            {
                _queued.Remove(key);
                if (_made.Count >= Capacity)
                {
                    _made = [];
                    Generation++;
                }
                try
                {
                    _made[key] = Make(label, pixels);
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NotSupportedException)
                {
                    // the label is left without a picture (an empty one, so it is not asked for again and again); the sketch is not harmed
                    log?.LogError(exception, "The picture of the label {Text} for the 3D view could not be made: {Reason}", label.Text, exception.Message);
                    _made[key] = new LabelBitmap(1, 1, new byte[4]);
                }
                drawAgain();
            }, DispatcherPriority.Background);
        }
        return null;
    }

    private LabelBitmap Make(SketchLabel label, int pixels)
    {
        Size text = painter.Measure(label, pixels);
        int width = (int)Math.Ceiling(text.Width) + 2 * Margin;
        int height = (int)Math.Ceiling(text.Height) + 2 * Margin;
        using RenderTargetBitmap bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        Surface surface = new Surface(painter, label, pixels, new Point(width / 2.0, height / 2.0)) { Width = width, Height = height };
        // grey scale smoothing: the colour fringes of the screen's own would show on any other colour than the one they were made for
        RenderOptions.SetTextRenderingMode(surface, TextRenderingMode.Antialias);
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));
        bitmap.Render(surface);
        byte[] rgba = new byte[width * height * 4];
        unsafe
        {
            fixed (byte* target = rgba)
            {
                bitmap.CopyPixels(new PixelRect(0, 0, width, height), (nint)target, rgba.Length, width * 4);
            }
        }
        if (bitmap.Format != PixelFormat.Rgba8888)
        {
            // blue, green, red, alpha in memory order: the card wants red first
            for (int i = 0; i < rgba.Length; i += 4)
            {
                (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
            }
        }
        return new LabelBitmap(width, height, rgba);
    }

    private sealed class Surface(SketchLabelPainter painter, SketchLabel label, int pixels, Point centre) : Control
    {
        public override void Render(DrawingContext context) => painter.Draw(context, label, centre, pixels);
    }
}
