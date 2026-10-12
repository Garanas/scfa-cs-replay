using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// The sketch as lines to draw (user stories N-1, N-3 and N-4): every view draws the shown sheets
    /// from this one function, so what a view draws is the same in the top view and in 3D. A line is a
    /// <see cref="SketchStroke"/> (places in ogrids, colour, width); a symbol is a few short lines in
    /// its colour. Labels are text and no lines: the top view draws them itself (<see cref="SketchLabels"/>).
    /// </summary>
    public static class SketchLines
    {
        /// <summary>
        /// The segments a circle is drawn with.
        /// </summary>
        private const int CircleSegments = 24;

        /// <summary>
        /// The sheets a view draws and how opaque each is: the shown sheets that hold something to draw,
        /// at their own opacity outside the Sketch mode and fully in it. A sheet that is fully
        /// transparent is left out.
        /// </summary>
        /// <param name="full">Whether the Sketch mode is the one chosen: every sheet is drawn fully.</param>
        public static IEnumerable<(SketchSheet Sheet, float Opacity)> Shown(Sketch sketch, bool full)
        {
            foreach (SketchSheet sheet in sketch.Sheets)
            {
                float opacity = full ? 1 : sheet.Opacity;
                if (sheet.Shown && !sheet.IsBlank && opacity > 0)
                {
                    yield return (sheet, opacity);
                }
            }
        }

        /// <summary>
        /// Every line of a sheet, in the order they are drawn: its strokes, then the lines of its
        /// symbols (which lie over the strokes).
        /// </summary>
        public static IEnumerable<SketchStroke> Of(SketchSheet sheet)
        {
            foreach (SketchStroke stroke in sheet.Strokes)
            {
                yield return stroke;
            }
            foreach (SketchSymbol symbol in sheet.Symbols)
            {
                foreach (SketchStroke line in Of(symbol))
                {
                    yield return line;
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
