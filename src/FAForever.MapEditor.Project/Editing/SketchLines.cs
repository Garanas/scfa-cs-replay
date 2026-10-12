using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// One line of the sketch to draw: places in ogrids, in a colour, at a width (<see cref="SketchLook.Pixels"/>
    /// on screen). A line of one place is a dot.
    /// </summary>
    public sealed record SketchLine(SketchColour Colour, SketchWidth Width, ImmutableArray<Vector2> Points);

    /// <summary>
    /// A sheet that shows, with the lines of what it holds and the opacity it is drawn at.
    /// </summary>
    public sealed record SketchLayer(SketchSheet Sheet, float Opacity, ImmutableArray<SketchLine> Lines);

    /// <summary>
    /// The sketch as lines (user stories N-1, N-3 and N-4): the one list every view draws from, so that the top
    /// view and the 3D view show the same. Whatever a sheet holds that is drawn with lines (strokes;
    /// symbols make a few short lines each, <see cref="Of(SketchSymbol)"/>) is turned into lines here; labels are text and no lines, the top view draws them itself (<see cref="SketchLabels"/>), in the order it is drawn in
    /// (a later line over an earlier).
    /// </summary>
    public static class SketchLines
    {
        /// <summary>
        /// The segments a circle is drawn with.
        /// </summary>
        private const int CircleSegments = 24;

        /// <summary>
        /// The lines of one sheet, in drawing order.
        /// </summary>
        public static ImmutableArray<SketchLine> Of(SketchSheet sheet) => Made.GetValue(sheet, held => new Held(Make(held))).Lines;

        // a sheet's lines, kept while the document holds that very sheet: the views ask every frame
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SketchSheet, Held> Made = [];

        private sealed record Held(ImmutableArray<SketchLine> Lines);

        private static ImmutableArray<SketchLine> Make(SketchSheet sheet)
        {
            ImmutableArray<SketchLine>.Builder lines = ImmutableArray.CreateBuilder<SketchLine>(sheet.Strokes.Length);
            foreach (SketchStroke stroke in sheet.Strokes)
            {
                if (!stroke.Points.IsDefaultOrEmpty)
                {
                    lines.Add(new SketchLine(stroke.Colour, stroke.Width, stroke.Points));
                }
            }
            // the symbols lie over the strokes, a few lines each
            foreach (SketchSymbol symbol in sheet.Symbols)
            {
                foreach (SketchStroke line in Of(symbol))
                {
                    lines.Add(new SketchLine(line.Colour, line.Width, line.Points));
                }
            }
            return lines.ToImmutable();
        }

        /// <summary>
        /// The opacity a sheet is drawn at: fully when it is the one <paramref name="chosen"/> in the Sketch
        /// mode, at its own opacity otherwise (the other sheets of the Sketch mode, and every sheet in the other
        /// modes, where <paramref name="chosen"/> is null).
        /// </summary>
        public static float OpacityOf(SketchSheet sheet, string? chosen) => chosen is not null && sheet.Name == chosen ? 1 : sheet.Opacity;

        /// <summary>
        /// The sheets that show and have something to draw, in the order they lie in (the first
        /// below), each with its lines and opacity.
        /// </summary>
        /// <param name="chosen">The sheet chosen in the Sketch mode, which draws fully; null outside the mode.</param>
        public static ImmutableArray<SketchLayer> Of(Sketch sketch, string? chosen)
        {
            ImmutableArray<SketchLayer>.Builder layers = ImmutableArray.CreateBuilder<SketchLayer>();
            foreach (SketchSheet sheet in sketch.Sheets)
            {
                float opacity = OpacityOf(sheet, chosen);
                if (!sheet.Shown || opacity <= 0)
                {
                    continue;
                }
                ImmutableArray<SketchLine> lines = Of(sheet);
                if (!lines.IsEmpty)
                {
                    layers.Add(new SketchLayer(sheet, opacity, lines));
                }
            }
            return layers.ToImmutable();
        }

        /// <summary>
        /// Whether <paramref name="later"/> is <paramref name="earlier"/> with lines added at its end: the
        /// same places in the same colours and widths, so that a view that drew the first only needs to
        /// draw the added ones.
        /// </summary>
        public static bool Extends(ImmutableArray<SketchLine> earlier, ImmutableArray<SketchLine> later)
        {
            if (later.Length < earlier.Length)
            {
                return false;
            }
            for (int i = 0; i < earlier.Length; i++)
            {
                if (earlier[i].Points != later[i].Points || earlier[i].Colour != later[i].Colour || earlier[i].Width != later[i].Width)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The sheets a view draws and how opaque each is: the shown sheets that hold something to draw,
        /// at their own opacity, except the sheet <paramref name="chosen"/> in the Sketch mode, which is drawn
        /// fully. A sheet that is fully transparent is left out.
        /// </summary>
        /// <param name="chosen">The sheet chosen in the Sketch mode; null outside the mode.</param>
        public static IEnumerable<(SketchSheet Sheet, float Opacity)> Shown(Sketch sketch, string? chosen)
        {
            foreach (SketchSheet sheet in sketch.Sheets)
            {
                float opacity = OpacityOf(sheet, chosen);
                if (sheet.Shown && !sheet.IsBlank && opacity > 0)
                {
                    yield return (sheet, opacity);
                }
            }
        }

        /// <summary>
        /// The lines a symbol is drawn with, in ogrids, all of <see cref="SketchWidth.Medium"/>: a spawn is a ring
        /// with a dot, a mass three rings inside one another, a hydrocarbon a star, an arrow a shaft with a head
        /// (turned by <see cref="SketchSymbol.Turn"/>), a cross two crossing lines, a question mark a hook, a
        /// stem and a dot.
        /// </summary>
        public static ImmutableArray<SketchStroke> Of(SketchSymbol symbol)
        {
            List<Vector2[]> shapes = [];
            switch (symbol.Kind)
            {
                case SketchSymbolKind.Spawn:
                    shapes.Add(Circle(0.5f));
                    shapes.Add([Vector2.Zero]);
                    break;
                case SketchSymbolKind.Mass:
                    shapes.Add(Circle(0.5f));
                    shapes.Add(Circle(0.33f));
                    shapes.Add(Circle(0.16f));
                    break;
                case SketchSymbolKind.Hydrocarbon:
                    Vector2[] star = new Vector2[11];
                    for (int i = 0; i < 10; i++)
                    {
                        float radius = i % 2 == 0 ? 0.5f : 0.2f;
                        float angle = -MathF.PI / 2 + i * MathF.PI / 5;
                        star[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                    }
                    star[10] = star[0];
                    shapes.Add(star);
                    break;
                case SketchSymbolKind.Arrow:
                    shapes.Add([new Vector2(-0.5f, 0), new Vector2(0.5f, 0)]);
                    shapes.Add([new Vector2(0.15f, -0.3f), new Vector2(0.5f, 0), new Vector2(0.15f, 0.3f)]);
                    break;
                case SketchSymbolKind.Cross:
                    shapes.Add([new Vector2(-0.4f, -0.4f), new Vector2(0.4f, 0.4f)]);
                    shapes.Add([new Vector2(-0.4f, 0.4f), new Vector2(0.4f, -0.4f)]);
                    break;
                case SketchSymbolKind.Question:
                    List<Vector2> hook = [];
                    for (int degrees = 180; degrees <= 450; degrees += 15)
                    {
                        float angle = degrees * MathF.PI / 180;
                        hook.Add(new Vector2(0.27f * MathF.Cos(angle), -0.2f + 0.27f * MathF.Sin(angle)));
                    }
                    hook.Add(new Vector2(0, 0.22f));
                    shapes.Add([.. hook]);
                    shapes.Add([new Vector2(0, 0.45f)]);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(symbol));
            }

            float turn = symbol.Kind == SketchSymbolKind.Arrow ? symbol.Turn * MathF.PI / 180 : 0;
            float cos = MathF.Cos(turn);
            float sin = MathF.Sin(turn);
            return [.. shapes.Select(shape => new SketchStroke(symbol.Colour, SketchWidth.Medium,
                [.. shape.Select(point => symbol.Place + symbol.Size * new Vector2(point.X * cos - point.Y * sin, point.X * sin + point.Y * cos))]))];
        }

        private static Vector2[] Circle(float radius)
        {
            Vector2[] points = new Vector2[CircleSegments + 1];
            for (int i = 0; i <= CircleSegments; i++)
            {
                float angle = i * 2 * MathF.PI / CircleSegments;
                points[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            }
            return points;
        }
    }
}
