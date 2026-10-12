using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using FAForever.MapEditor.App.Diagnostics;
using FAForever.MapEditor.App.ViewModels;
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
    private sealed record SketchRun(SketchColour Colour, SketchWidth Width, Geometry Geometry);

    // a sheet's runs, kept while the document holds that very sheet
    private static readonly ConditionalWeakTable<SketchSheet, SketchRun[]> SketchRuns = [];

    private readonly Dictionary<SketchColour, SolidColorBrush> _sketchInks = [];

    /// <summary>
    /// How many strokes the last frame drew, and how many sheets (for tests and automation).
    /// </summary>
    public (int Sheets, int Strokes, int Pending) SketchDrawn { get; private set; }

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
        bool full = model.InSketchMode;
        if (model.ShowSketch)
        {
            foreach (SketchSheet sheet in document.Sketch.Sheets)
            {
                if (!sheet.Shown || sheet.Strokes.IsEmpty)
                {
                    continue;
                }
                float opacity = full ? 1 : sheet.Opacity;
                if (opacity <= 0)
                {
                    continue;
                }
                sheets++;
                strokes += sheet.Strokes.Length;
                SketchRun[] runs = SketchRuns.GetValue(sheet, BuildSketchRuns);
                using (context.PushOpacity(opacity))
                using (context.PushTransform(SketchMatrix()))
                {
                    foreach (SketchRun run in runs)
                    {
                        context.DrawGeometry(null, SketchPen(run.Colour, run.Width, dashed: false), run.Geometry);
                    }
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
                    context.DrawGeometry(null, SketchPen(stroke.Colour, stroke.Width, dashed: i > 0), GeometryOf(stroke));
                }
            }
        }
        SketchDrawn = (sheets, strokes, pending);
        if (model.ShowSketch)
        {
            DrawSketchKey(context, document, model);
        }
    }

    // the map's place of an ogrid to the view's, as the view's own transform
    private Matrix SketchMatrix() => Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_offset.X, _offset.Y);

    private Pen SketchPen(SketchColour colour, SketchWidth width, bool dashed) =>
        new Pen(SketchBrush(colour), SketchLook.Pixels(width) / _zoom, dashed ? new DashStyle([2, 2], 0) : null, PenLineCap.Round, PenLineJoin.Round);

    private static SketchRun[] BuildSketchRuns(SketchSheet sheet)
    {
        using Performance.Span measure = Performance.Measure("top.sketch-build");
        List<SketchRun> runs = [];
        int from = 0;
        while (from < sheet.Strokes.Length)
        {
            SketchStroke first = sheet.Strokes[from];
            int to = from;
            while (to < sheet.Strokes.Length && sheet.Strokes[to].Colour == first.Colour && sheet.Strokes[to].Width == first.Width)
            {
                to++;
            }
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext figures = geometry.Open())
            {
                for (int i = from; i < to; i++)
                {
                    AddFigure(figures, sheet.Strokes[i]);
                }
            }
            runs.Add(new SketchRun(first.Colour, first.Width, geometry));
            from = to;
        }
        return [.. runs];
    }

    private static StreamGeometry GeometryOf(SketchStroke stroke)
    {
        StreamGeometry geometry = new StreamGeometry();
        using (StreamGeometryContext figures = geometry.Open())
        {
            AddFigure(figures, stroke);
        }
        return geometry;
    }

    private static void AddFigure(StreamGeometryContext figures, SketchStroke stroke)
    {
        if (stroke.Points.IsDefaultOrEmpty)
        {
            return;
        }
        Point first = new Point(stroke.Points[0].X, stroke.Points[0].Y);
        figures.BeginFigure(first, false);
        if (stroke.Points.Length == 1)
        {
            // a dot is a line of no length: the round ends make it a disc
            figures.LineTo(first);
        }
        for (int i = 1; i < stroke.Points.Length; i++)
        {
            figures.LineTo(new Point(stroke.Points[i].X, stroke.Points[i].Y));
        }
        figures.EndFigure(false);
    }

    /// <summary>
    /// The legends of the shown sheets, as a small key in the view's lower left corner: a dot in the
    /// colour and what it means, under the sheet's name when more than one sheet has a legend.
    /// </summary>
    private void DrawSketchKey(DrawingContext context, MapDocument document, MainViewModel model)
    {
        IReadOnlyList<(string Sheet, IReadOnlyList<(SketchColour Colour, string Text)> Entries)> keys = model.Sketch.Keys(document);
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
        Rect box = new Rect(gap * 2, Bounds.Height - height - gap * 2, width, height);
        context.DrawRectangle(Token<IBrush>("SurfaceBrush"), new Pen(Token<IBrush>("InputStrokeBrush"), 1), box, 4, 4);
        double y = box.Y + gap;
        foreach ((FormattedText row, SketchColour? colour) in rows)
        {
            double x = box.X + gap;
            if (colour is SketchColour swatch)
            {
                context.DrawEllipse(SketchBrush(swatch), new Pen(Token<IBrush>("InputStrokeBrush"), 1), new Point(x + dot, y + rowHeight / 2 - gap / 4), dot, dot);
                x += dot * 2 + gap;
            }
            context.DrawText(row, new Point(x, y));
            y += rowHeight;
        }
    }
}
