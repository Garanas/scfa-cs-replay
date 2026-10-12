using System.Collections.Immutable;
using System.Numerics;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Which kind of thing of a sheet an item is (user story N-4).
    /// </summary>
    public enum SketchItemKind
    {
        Stroke,
        Symbol,
        Label,
    }

    /// <summary>
    /// One stroke, symbol or label of a sheet, by its place in the sheet's list of that kind: what the
    /// Select tool selects. It is valid for the version of the sheet it was made from; edits that
    /// remove things renumber the rest.
    /// </summary>
    public readonly record struct SketchItem(SketchItemKind Kind, int Index);

    /// <summary>
    /// Where a label lies (user story N-4). The text is drawn by the view in the hand-written font, so the
    /// extent here is an estimate from its length: enough to click on a label, box it and rub it out.
    /// </summary>
    public static class SketchLabels
    {
        /// <summary>
        /// The width of a letter as a part of the label's size.
        /// </summary>
        public const float LetterWidth = 0.42f;

        /// <summary>
        /// The box of a label in ogrids: the text centred on its place.
        /// </summary>
        public static (Vector2 Min, Vector2 Max) Bounds(SketchLabel label)
        {
            Vector2 half = new Vector2(Math.Max(label.Text.Length, 1) * label.Size * LetterWidth / 2, label.Size * 0.6f);
            return (label.Place - half, label.Place + half);
        }
    }

    /// <summary>
    /// Finding what lies at a place or in a box on a sheet (user story N-4), for the Select tool, and
    /// where the eraser's path touches a symbol or a label. Places are in ogrids.
    /// </summary>
    public static class SketchPicking
    {
        /// <summary>
        /// The box of a symbol in ogrids.
        /// </summary>
        public static (Vector2 Min, Vector2 Max) Bounds(SketchSymbol symbol) =>
            (symbol.Place - new Vector2(symbol.Size / 2), symbol.Place + new Vector2(symbol.Size / 2));

        /// <summary>
        /// The box of an item of a sheet.
        /// </summary>
        public static (Vector2 Min, Vector2 Max) Bounds(SketchSheet sheet, SketchItem item)
        {
            switch (item.Kind)
            {
                case SketchItemKind.Symbol:
                    return Bounds(sheet.Symbols[item.Index]);
                case SketchItemKind.Label:
                    return SketchLabels.Bounds(sheet.Labels[item.Index]);
                default:
                    Vector2 min = new Vector2(float.MaxValue);
                    Vector2 max = new Vector2(float.MinValue);
                    foreach (Vector2 point in sheet.Strokes[item.Index].Points)
                    {
                        min = Vector2.Min(min, point);
                        max = Vector2.Max(max, point);
                    }
                    return (min, max);
            }
        }

        /// <summary>
        /// Whether an item still exists in a sheet.
        /// </summary>
        public static bool Exists(SketchSheet sheet, SketchItem item) => item.Index >= 0 && item.Kind switch
        {
            SketchItemKind.Stroke => item.Index < sheet.Strokes.Length,
            SketchItemKind.Symbol => item.Index < sheet.Symbols.Length,
            _ => item.Index < sheet.Labels.Length,
        };

        /// <summary>
        /// The topmost item within <paramref name="reach"/> of a place: what is drawn last (labels over symbols
        /// over strokes, later over earlier); null for nothing.
        /// </summary>
        /// <param name="reach">How near a stroke must pass, and how far beyond a symbol's or a label's box the
        /// place may lie; the view passes a few pixels.</param>
        public static SketchItem? Pick(SketchSheet sheet, Vector2 place, float reach)
        {
            for (int i = sheet.Labels.Length - 1; i >= 0; i--)
            {
                if (Inside(SketchLabels.Bounds(sheet.Labels[i]), place, reach))
                {
                    return new SketchItem(SketchItemKind.Label, i);
                }
            }
            for (int i = sheet.Symbols.Length - 1; i >= 0; i--)
            {
                if (Inside(Bounds(sheet.Symbols[i]), place, reach))
                {
                    return new SketchItem(SketchItemKind.Symbol, i);
                }
            }
            for (int i = sheet.Strokes.Length - 1; i >= 0; i--)
            {
                if (Near(sheet.Strokes[i].Points, place, reach))
                {
                    return new SketchItem(SketchItemKind.Stroke, i);
                }
            }
            return null;
        }

        /// <summary>
        /// The items a box takes in: a stroke that passes through it, a symbol whose centre lies in it, a label
        /// whose box meets it. Strokes first, then symbols, then labels, each in the sheet's order.
        /// </summary>
        public static ImmutableArray<SketchItem> InBox(SketchSheet sheet, Vector2 corner, Vector2 other)
        {
            Vector2 min = Vector2.Min(corner, other);
            Vector2 max = Vector2.Max(corner, other);
            ImmutableArray<SketchItem>.Builder items = ImmutableArray.CreateBuilder<SketchItem>();
            for (int i = 0; i < sheet.Strokes.Length; i++)
            {
                if (CrossesBox(sheet.Strokes[i].Points, min, max))
                {
                    items.Add(new SketchItem(SketchItemKind.Stroke, i));
                }
            }
            for (int i = 0; i < sheet.Symbols.Length; i++)
            {
                Vector2 at = sheet.Symbols[i].Place;
                if (at.X >= min.X && at.X <= max.X && at.Y >= min.Y && at.Y <= max.Y)
                {
                    items.Add(new SketchItem(SketchItemKind.Symbol, i));
                }
            }
            for (int i = 0; i < sheet.Labels.Length; i++)
            {
                (Vector2 low, Vector2 high) = SketchLabels.Bounds(sheet.Labels[i]);
                if (low.X <= max.X && high.X >= min.X && low.Y <= max.Y && high.Y >= min.Y)
                {
                    items.Add(new SketchItem(SketchItemKind.Label, i));
                }
            }
            return items.ToImmutable();
        }

        /// <summary>
        /// Whether a box lies within a radius of the segment from <paramref name="from"/> to <paramref name="to"/>:
        /// what an eraser of that radius passing along it reaches of a symbol or a label.
        /// </summary>
        public static bool Reaches((Vector2 Min, Vector2 Max) box, Vector2 from, Vector2 to, float radius)
        {
            if (SegmentMeetsBox(from, to, box.Min, box.Max))
            {
                return true;
            }
            float r2 = radius * radius;
            if (DistanceSquaredToBox(from, box) <= r2 || DistanceSquaredToBox(to, box) <= r2)
            {
                return true;
            }
            foreach (Vector2 corner in (ReadOnlySpan<Vector2>)[box.Min, new Vector2(box.Max.X, box.Min.Y), box.Max, new Vector2(box.Min.X, box.Max.Y)])
            {
                if (SketchGeometry.DistanceToSegment(corner, from, to) <= radius)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool Inside((Vector2 Min, Vector2 Max) box, Vector2 place, float reach) =>
            place.X >= box.Min.X - reach && place.X <= box.Max.X + reach && place.Y >= box.Min.Y - reach && place.Y <= box.Max.Y + reach;

        private static bool Near(ImmutableArray<Vector2> line, Vector2 place, float reach)
        {
            if (line.Length == 1)
            {
                return Vector2.Distance(line[0], place) <= reach;
            }
            for (int i = 0; i + 1 < line.Length; i++)
            {
                if (SketchGeometry.DistanceToSegment(place, line[i], line[i + 1]) <= reach)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool CrossesBox(ImmutableArray<Vector2> line, Vector2 min, Vector2 max)
        {
            if (line.Length == 1)
            {
                return line[0].X >= min.X && line[0].X <= max.X && line[0].Y >= min.Y && line[0].Y <= max.Y;
            }
            for (int i = 0; i + 1 < line.Length; i++)
            {
                if (SegmentMeetsBox(line[i], line[i + 1], min, max))
                {
                    return true;
                }
            }
            return false;
        }

        // Liang and Barsky: the parts of the segment's parameter inside each slab of the box
        private static bool SegmentMeetsBox(Vector2 p, Vector2 q, Vector2 min, Vector2 max)
        {
            float low = 0;
            float high = 1;
            Vector2 d = q - p;
            return Clip(p.X, d.X, min.X, max.X, ref low, ref high) && Clip(p.Y, d.Y, min.Y, max.Y, ref low, ref high);
        }

        private static bool Clip(float start, float step, float minimum, float maximum, ref float low, ref float high)
        {
            if (MathF.Abs(step) < 1e-9f)
            {
                return start >= minimum && start <= maximum;
            }
            float a = (minimum - start) / step;
            float b = (maximum - start) / step;
            low = Math.Max(low, Math.Min(a, b));
            high = Math.Min(high, Math.Max(a, b));
            return low <= high;
        }

        private static float DistanceSquaredToBox(Vector2 place, (Vector2 Min, Vector2 Max) box)
        {
            Vector2 nearest = Vector2.Clamp(place, box.Min, box.Max);
            return Vector2.DistanceSquared(place, nearest);
        }
    }
}
