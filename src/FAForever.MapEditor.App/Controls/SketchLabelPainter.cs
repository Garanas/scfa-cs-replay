using System.Globalization;
using Avalonia;
using Avalonia.Media;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// Draws the sketch's labels (user story N-4) in the hand-written font, centred on a point, in the label's colour over a halo
/// of the colour that contrasts with it, so that the text reads on any terrain. The top view and the 3D view's overlay
/// share it; the texts are made once for a text, colour and size on screen.
/// </summary>
public sealed class SketchLabelPainter(Func<Typeface> typeface)
{
    private readonly Dictionary<(string Text, SketchColour Colour, int Pixels), (FormattedText Ink, FormattedText Halo)> _texts = [];
    private readonly Dictionary<SketchColour, SolidColorBrush> _inks = [];
    private Typeface? _font;

    /// <summary>
    /// A label smaller than this on screen, in pixels, is not drawn: it could not be read.
    /// </summary>
    public const double Smallest = 4;

    /// <summary>
    /// Draws one label.
    /// </summary>
    /// <param name="centre">Where the text's centre lies, in the control's pixels.</param>
    /// <param name="pixels">The text's size on screen.</param>
    public void Draw(DrawingContext context, SketchLabel label, Point centre, int pixels)
    {
        if (_texts.Count > 512)
        {
            _texts.Clear();
        }
        _font ??= typeface();
        if (!_texts.TryGetValue((label.Text, label.Colour, pixels), out (FormattedText Ink, FormattedText Halo) text))
        {
            SketchColour halo = label.Colour == SketchColour.Black ? SketchColour.White : SketchColour.Black;
            text = (Make(label.Text, pixels, Ink(label.Colour)), Make(label.Text, pixels, Ink(halo)));
            _texts[(label.Text, label.Colour, pixels)] = text;
        }
        Point at = new Point(centre.X - text.Ink.Width / 2, centre.Y - text.Ink.Height / 2);
        foreach ((double dx, double dz) in (ReadOnlySpan<(double, double)>)[(-1, 0), (1, 0), (0, -1), (0, 1)])
        {
            context.DrawText(text.Halo, new Point(at.X + dx, at.Y + dz));
        }
        context.DrawText(text.Ink, at);
    }

    /// <summary>
    /// How large the text of a label is when drawn at <paramref name="pixels"/>, without its halo.
    /// </summary>
    public Size Measure(SketchLabel label, int pixels)
    {
        _font ??= typeface();
        FormattedText text = Make(label.Text, pixels, Ink(label.Colour));
        return new Size(text.Width, text.Height);
    }

    private SolidColorBrush Ink(SketchColour colour)
    {
        if (!_inks.TryGetValue(colour, out SolidColorBrush? brush))
        {
            brush = new SolidColorBrush(Color.FromUInt32(SketchLook.Argb(colour)));
            _inks[colour] = brush;
        }
        return brush;
    }

    private FormattedText Make(string text, double pixels, IBrush brush) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _font!.Value, pixels, brush);
}
