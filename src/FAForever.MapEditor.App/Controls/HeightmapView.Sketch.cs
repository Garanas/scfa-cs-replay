using System.Runtime.CompilerServices;
using System.Collections.Immutable;
using Avalonia;
using Avalonia.Media;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
using FAForever.MapEditor.Project.Editing;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.App.Controls;

/// <summary>
/// The map maker's sketch in the top view (user story N-1): the strokes of the shown sheets as lines
/// of a fixed width on screen at every zoom, with round ends and joins, in the sketch's six colours;
/// outside the Sketch mode at each sheet's opacity; the line being drawn with its images by the
/// symmetry; and a key in a corner that lists the legends of the shown sheets.
/// </summary>
/// <remarks>
/// A stroke's places are in ogrids, so a sheet's lines are built once into geometries in the map's
/// own coordinates (consecutive strokes of one colour and width are one geometry, which keeps the
/// order they were drawn in) and drawn through the view's transform; the pen's thickness is divided
/// by the zoom so that it stays the same number of pixels. The geometries follow the sheet: another
/// version of it builds them again.
/// </remarks>
public sealed partial class HeightmapView
{
    /// <summary>
    /// Strokes of one colour and width that follow each other in a sheet, built as one geometry; at
    /// most <see cref="StrokesPerRun"/>, so that a view zoomed in on a part of a large sketch draws only the
    /// runs that lie in it (<paramref name="Bounds"/>, in ogrids).
    /// </summary>
    private sealed record SketchRun(SketchColour Colour, SketchWidth Width, Geometry Geometry, Rect Bounds, int Strokes);

    private const int StrokesPerRun = 24;

    // a sheet's runs, kept while the document holds that very sheet
    private static readonly ConditionalWeakTable<SketchSheet, SketchRun[]> SketchRuns = [];

    private readonly Dictionary<SketchColour, SolidColorBrush> _sketchInks = [];

    /// <summary>
    /// How many strokes the last frame drew, and how many sheets (for tests and automation).
    /// </summary>
    public (int Sheets, int Strokes, int Culled, int Pending, int Symbols, int Labels) SketchDrawn { get; private set; }

    private SolidColorBrush SketchBrush(SketchColour colour)
    {
        if (!_sketchInks.TryGetValue(colour, out SolidColorBrush? brush))
        {
            brush = new SolidColorBrush(Color.FromUInt32(SketchLook.Argb(colour)));
            _sketchInks[colour] = brush;
        }
        return brush;
    }

    private void DrawSketch(DrawingContext context, MapDocument document, MainViewModel model)
    {
        using Performance.Span measure = Performance.Measure("top.sketch");
        int sheets = 0;
        int strokes = 0;
        int culled = 0;
        int symbols = 0;
        int labels = 0;
        bool full = model.InSketchMode;
        // what lies in the view, in ogrids, with a stroke's half width around it
        Rect visible = new Rect(ControlToMap(default), ControlToMap(new Point(Bounds.Width, Bounds.Height)));
        visible = visible.Inflate(SketchLook.Pixels(SketchWidth.Thick) / _zoom);
        if (model.ShowSketch)
        {
            foreach ((SketchSheet sheet, float opacity) in SketchLines.Shown(document.Sketch, full))
            {
                sheets++;
                strokes += sheet.Strokes.Length;
                symbols += sheet.Symbols.Length;
                SketchRun[] runs = SketchRuns.GetValue(sheet, BuildSketchRuns);
                // a sheet at full opacity needs no layer of its own
                using (opacity < 1 ? context.PushOpacity(opacity) : (IDisposable?)null)
                {
                    using (context.PushTransform(SketchMatrix()))
                    {
                        // the halos of the black and white lines first, under every line of the sheet
                        foreach (SketchRun run in runs)
                        {
                            if (SketchLook.HaloArgb(run.Colour) is not null && run.Bounds.Intersects(visible))
                            {
                                context.DrawGeometry(null, SketchPen(run.Colour, run.Width, dashed: false, halo: true), run.Geometry);
                            }
                        }
                        foreach (SketchRun run in runs)
                        {
                            if (!run.Bounds.Intersects(visible))
                            {
                                culled += run.Strokes;
                                continue;
                            }
                            context.DrawGeometry(null, SketchPen(run.Colour, run.Width, dashed: false), run.Geometry);
                        }
                    }
                    // the labels are text: drawn at their size on screen, over the lines
                    labels += DrawLabels(context, sheet, visible);
                }
            }
        }

        int pending = model.Sketch.Pending.Count;
        if (pending > 0)
        {
            using (context.PushTransform(SketchMatrix()))
            {
                for (int i = 0; i < pending; i++)
                {
                    SketchStroke stroke = model.Sketch.Pending[i];
                    // the first is the line the mouse draws, the others its images by the symmetry
                    StreamGeometry geometry = GeometryOf(stroke);
                    if (SketchLook.HaloArgb(stroke.Colour) is not null)
                    {
                        context.DrawGeometry(null, SketchPen(stroke.Colour, stroke.Width, dashed: i > 0, halo: true), geometry);
                    }
                    context.DrawGeometry(null, SketchPen(stroke.Colour, stroke.Width, dashed: i > 0), geometry);
                }
            }
        }
        int pendingSymbols = model.Sketch.PendingSymbols.Count;
        if (pendingSymbols > 0)
        {
            using (context.PushTransform(SketchMatrix()))
            {
                for (int i = 0; i < pendingSymbols; i++)
                {
                    // the first is the symbol the mouse places, the others its images by the symmetry
                    SketchStroke[] lines = [.. SketchLines.Of(model.Sketch.PendingSymbols[i])];
                    foreach (SketchStroke line in lines.Where(line => SketchLook.HaloArgb(line.Colour) is not null))
                    {
                        context.DrawGeometry(null, SketchPen(line.Colour, line.Width, dashed: i > 0, halo: true), GeometryOf(line));
                    }
                    foreach (SketchStroke line in lines)
                    {
                        context.DrawGeometry(null, SketchPen(line.Colour, line.Width, dashed: i > 0), GeometryOf(line));
                    }
                }
            }
        }
        SketchDrawn = (sheets, strokes, culled, pending + pendingSymbols, symbols, labels);
        if (model.ShowSketch)
        {
            DrawSketchKey(context, document, model);
        }
    }

    private SketchLabelPainter? _labelPainter;

    /// <summary>
    /// Draws the labels of a sheet in the hand-written font (the token <c>HandFontFamily</c>), centred on their
    /// places, at their size on the map (so they grow with the zoom), over a halo of the colour that contrasts
    /// with their own so that they read on any terrain.
    /// </summary>
    /// <returns>How many labels were drawn.</returns>
    private int DrawLabels(DrawingContext context, SketchSheet sheet, Rect visible)
    {
        if (sheet.Labels.IsEmpty)
        {
            return 0;
        }
        _labelPainter ??= new SketchLabelPainter(() => new Typeface(Token<FontFamily>("HandFontFamily"), FontStyle.Normal, FontWeight.Bold));
        int drawn = 0;
        foreach (SketchLabel label in sheet.Labels)
        {
            double pixels = label.Size * _zoom;
            (System.Numerics.Vector2 low, System.Numerics.Vector2 high) = SketchLabels.Bounds(label);
            if (pixels < SketchLabelPainter.Smallest || !new Rect(new Point(low.X, low.Y), new Point(high.X, high.Y)).Intersects(visible))
            {
                continue;
            }
            _labelPainter.Draw(context, label, MapToControl(label.Place.X, label.Place.Y), (int)Math.Round(pixels));
            drawn++;
        }
        return drawn;
    }

    // the map's place of an ogrid to the view's, as the view's own transform
    private Matrix SketchMatrix() => Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_offset.X, _offset.Y);

    // a halo is the pen of the opposite tone (black under white, white under black) a little wider than the line
    private Pen SketchPen(SketchColour colour, SketchWidth width, bool dashed, bool halo = false) =>
        new Pen(halo ? SketchBrush(SketchHaloColour(colour)) : SketchBrush(colour),
            (SketchLook.Pixels(width) + (halo ? 2 * SketchLook.HaloPixels : 0)) / _zoom,
            dashed ? new DashStyle([2, 2], 0) : null, PenLineCap.Round, PenLineJoin.Round);

    private static SketchColour SketchHaloColour(SketchColour colour) => colour == SketchColour.Black ? SketchColour.White : SketchColour.Black;

    private static SketchRun[] BuildSketchRuns(SketchSheet sheet)
    {
        using Performance.Span measure = Performance.Measure("top.sketch-build");
        // the strokes, then the lines of the symbols: the one function the 3D view draws from too
        ImmutableArray<SketchLine> lines = SketchLines.Of(sheet);
        List<SketchRun> runs = [];
        int from = 0;
        while (from < lines.Length)
        {
            SketchLine first = lines[from];
            int to = from;
            while (to < lines.Length && to - from < StrokesPerRun && lines[to].Colour == first.Colour && lines[to].Width == first.Width)
            {
                to++;
            }
            StreamGeometry geometry = new StreamGeometry();
            float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
            using (StreamGeometryContext figures = geometry.Open())
            {
                for (int i = from; i < to; i++)
                {
                    AddFigure(figures, lines[i].Points);
                    foreach (System.Numerics.Vector2 point in lines[i].Points)
                    {
                        left = Math.Min(left, point.X);
                        top = Math.Min(top, point.Y);
                        right = Math.Max(right, point.X);
                        bottom = Math.Max(bottom, point.Y);
                    }
                }
            }
            runs.Add(new SketchRun(first.Colour, first.Width, geometry, new Rect(new Point(left, top), new Point(right, bottom)), to - from));
            from = to;
        }
        return [.. runs];
    }

    private static StreamGeometry GeometryOf(SketchStroke stroke)
    {
        StreamGeometry geometry = new StreamGeometry();
        using (StreamGeometryContext figures = geometry.Open())
        {
            AddFigure(figures, stroke.Points);
        }
        return geometry;
    }

    private static void AddFigure(StreamGeometryContext figures, ImmutableArray<System.Numerics.Vector2> points)
    {
        if (points.IsDefaultOrEmpty)
        {
            return;
        }
        Point first = new Point(points[0].X, points[0].Y);
        figures.BeginFigure(first, false);
        if (points.Length == 1)
        {
            // a dot is a line of no length: the round ends make it a disc
            figures.LineTo(first);
        }
        for (int i = 1; i < points.Length; i++)
        {
            figures.LineTo(new Point(points[i].X, points[i].Y));
        }
        figures.EndFigure(false);
    }

    /// <summary>
    /// The legends of the shown sheets, as a small key in the view's lower left corner: a dot in the
    /// colour and what it means, under the sheet's name when more than one sheet has a legend.
    /// </summary>
    // the key takes at most this share of the map's width on screen, and is never scaled below this
    private const double KeyShareOfMap = 0.4, KeyLeastScale = 0.5;

    /// <summary>
    /// Where the last frame drew the legend's key, in the view's pixels (empty for none), and how opaque (for tests and automation).
    /// </summary>
    public (Rect Box, double Opacity) SketchKeyDrawn { get; private set; }

    private void DrawSketchKey(DrawingContext context, MapDocument document, MainViewModel model)
    {
        SketchKeyDrawn = default;
        IReadOnlyList<(string Sheet, IReadOnlyList<(SketchColour Colour, string Text)> Entries)> keys = model.Sketch.Keys(document, model.InSketchMode);
        if (keys.Count == 0)
        {
            return;
        }
        Typeface font = new Typeface(Token<FontFamily>("SansFontFamily"));
        double size = Token<double>("FontSizeMapLabel");
        IBrush text = Token<IBrush>("TextPrimaryBrush");
        IBrush heading = Token<IBrush>("TextSecondaryBrush");
        double gap = Token<double>("SpaceSmall");
        double dot = size * 0.5;
        bool named = keys.Count > 1;

        List<(FormattedText Text, SketchColour? Colour)> rows = [];
        double width = 0;
        foreach ((string sheet, IReadOnlyList<(SketchColour Colour, string Text)> entries) in keys)
        {
            if (named)
            {
                rows.Add((new FormattedText(sheet, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(font.FontFamily, FontStyle.Normal, FontWeight.Bold), size, heading), null));
            }
            foreach ((SketchColour colour, string label) in entries)
            {
                rows.Add((new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, font, size, text), colour));
            }
        }
        double rowHeight = rows.Max(row => row.Text.Height) + gap / 2;
        width = rows.Max(row => row.Text.Width + (row.Colour is null ? 0 : dot * 2 + gap)) + gap * 2;
        double height = rows.Count * rowHeight + gap * 2;
        // the key follows the sheets' opacity outside the Sketch mode, and keeps to a corner: it shrinks with the map
        // when that is zoomed out (never below half) and takes at most a third of the view
        double opacity = model.InSketchMode ? 1 : document.Sketch.Sheets.Where(sheet => keys.Any(key => key.Sheet == sheet.Name)).Max(sheet => sheet.Opacity);
        double scale = Math.Clamp(Math.Min(document.Terrain.Heightmap.Samples.Width * _zoom * KeyShareOfMap / (width + gap * 4), Math.Min(Bounds.Width, Bounds.Height) / 3 / Math.Max(width, height)), KeyLeastScale, 1);
        Rect box = new Rect(0, 0, width, height);
        Point corner = new Point(gap * 2, Bounds.Height - height * scale - gap * 2);
        SketchKeyDrawn = (new Rect(corner.X, corner.Y, width * scale, height * scale), opacity);
        using IDisposable placed = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(corner.X, corner.Y));
        using IDisposable faded = context.PushOpacity(opacity);
        context.DrawRectangle(Token<IBrush>("SurfaceBrush"), new Pen(Token<IBrush>("InputStrokeBrush"), 1), box, 4, 4);
        double y = box.Y + gap;
        foreach ((FormattedText row, SketchColour? colour) in rows)
        {
            double x = box.X + gap;
            if (colour is SketchColour swatch)
            {
                context.DrawEllipse(SketchBrush(swatch), new Pen(new SolidColorBrush(Color.FromUInt32(SketchLook.RimArgb(swatch))), 1), new Point(x + dot, y + rowHeight / 2 - gap / 4), dot, dot);
                x += dot * 2 + gap;
            }
            context.DrawText(row, new Point(x, y));
            y += rowHeight;
        }
    }
}
