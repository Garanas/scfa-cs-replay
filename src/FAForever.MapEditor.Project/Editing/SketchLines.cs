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
    /// The sketch as lines (user story N-1, N-3): the one list every view draws from, so that the top
    /// view and the 3D view show the same. Whatever a sheet holds that is drawn with lines (strokes;
    /// symbols make a few short lines each) is turned into lines here, in the order it is drawn in
    /// (a later line over an earlier).
    /// </summary>
    public static class SketchLines
    {
        /// <summary>
        /// The lines of one sheet, in drawing order.
        /// </summary>
        public static ImmutableArray<SketchLine> Of(SketchSheet sheet)
        {
            ImmutableArray<SketchLine>.Builder lines = ImmutableArray.CreateBuilder<SketchLine>(sheet.Strokes.Length);
            foreach (SketchStroke stroke in sheet.Strokes)
            {
                if (!stroke.Points.IsDefaultOrEmpty)
                {
                    lines.Add(new SketchLine(stroke.Colour, stroke.Width, stroke.Points));
                }
            }
            return lines.ToImmutable();
        }

        /// <summary>
        /// The opacity a sheet is drawn at: fully in the Sketch mode (<paramref name="full"/>), at its own
        /// in the other modes.
        /// </summary>
        public static float OpacityOf(SketchSheet sheet, bool full) => full ? 1 : sheet.Opacity;

        /// <summary>
        /// The sheets that show and have something to draw, in the order they lie in (the first
        /// below), each with its lines and opacity.
        /// </summary>
        /// <param name="full">Whether the Sketch mode is the one chosen: every sheet then draws fully.</param>
        public static ImmutableArray<SketchLayer> Of(Sketch sketch, bool full)
        {
            ImmutableArray<SketchLayer>.Builder layers = ImmutableArray.CreateBuilder<SketchLayer>();
            foreach (SketchSheet sheet in sketch.Sheets)
            {
                float opacity = OpacityOf(sheet, full);
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
    }
}
