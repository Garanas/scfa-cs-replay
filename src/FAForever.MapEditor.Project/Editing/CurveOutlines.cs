using System.Numerics;
using FAForever.FileFormats.Map;
using FAForever.MapEditor.Project.Files.InMemory;

namespace FAForever.MapEditor.Project.Editing
{
    /// <summary>
    /// Which line of a curve's outline (<see cref="CurveOutlines.Of"/>).
    /// </summary>
    public enum CurveOutlineKind
    {
        /// <summary>
        /// Where the profile is at its full height: the width's edge (a cliff: its high side's; a
        /// fill: the rim outside the line).
        /// </summary>
        Edge,

        /// <summary>
        /// Where the fade ends: beyond it the curve shapes nothing.
        /// </summary>
        Fade,
    }

    /// <summary>
    /// A line along a curve that shows how far it reaches (user story T-4): the window draws an edge
    /// dashed and a fade dotted.
    /// </summary>
    /// <param name="Points">Places in ogrids, about an ogrid apart.</param>
    /// <param name="Part">0 for the curve itself, 1 and more for its images by the symmetry.</param>
    public sealed record CurveOutline(IReadOnlyList<Vector2> Points, CurveOutlineKind Kind, int Part);

    /// <summary>
    /// The width and fade lines of a curve (user story T-4), from its samples
    /// (<see cref="HeightCurves.Samples"/>: the widths and falloffs per point with the variation
    /// along the curve), as the rasterizer reads them: the left of the direction of travel in the
    /// top view is a cliff's high side; a fill's outside lies on the side its area says. Edge noise
    /// is left out: the lines show the curve's reach without it.
    /// </summary>
    public static class CurveOutlines
    {
        /// <summary>
        /// The lines of a curve and of its images by the symmetry on a map of that size: for a line,
        /// the width's edge and the fade's end on both sides; for a cliff, its high side's (the
        /// width or half the face, whichever is more) and its fade back; for a fill, the rim outside
        /// (and a closed cliff's face: its inner and outer side).
        /// </summary>
        public static IReadOnlyList<CurveOutline> Of(HeightCurve curve, int mapWidth, int mapHeight)
        {
            IReadOnlyList<CurveSample> samples = HeightCurves.Samples(curve);
            if (samples.Count < 2)
            {
                return [];
            }
            List<CurveOutline> outlines = [];
            AddPart(curve, [.. samples], 0, outlines);
            if (curve.Symmetry is SymmetrySetting symmetry)
            {
                int part = 1;
                foreach (MapSymmetryTransform transform in MapSymmetry.Transforms(symmetry.Kind, symmetry.Count, mapWidth, mapHeight).Skip(1))
                {
                    // as the rasterizer: the curve's own samples turned or mirrored, a mirrored image running the other way
                    CurveSample[] image = [.. samples.Select(sample =>
                    {
                        Vector2 at = transform.Apply(new Vector2(sample.X, sample.Z));
                        return sample with { X = at.X, Z = at.Y };
                    })];
                    if (transform.IsMirror)
                    {
                        Array.Reverse(image);
                    }
                    AddPart(curve, image, part++, outlines);
                }
            }
            return outlines;
        }

        private static void AddPart(HeightCurve curve, CurveSample[] line, int part, List<CurveOutline> outlines)
        {
            void Add(Func<CurveSample, float> left, CurveOutlineKind kind)
            {
                outlines.Add(new CurveOutline(Offset(line, left, curve.Closed), kind, part));
            }
            float face = Math.Max(0, curve.CliffFace) / 2;
            if (curve.Fills)
            {
                // the outside's side: its area's sign, as the rasterizer's fill
                float area = 0;
                for (int i = 0; i + 1 < line.Length; i++)
                {
                    area += line[i].X * line[i + 1].Z - line[i + 1].X * line[i].Z;
                }
                float outside = area > 0 ? 1 : -1;
                if (curve.Profile == CurveProfile.Cliff)
                {
                    // a closed cliff: its face about the line, full inside it, nothing beyond it outside
                    Add(_ => -outside * face, CurveOutlineKind.Edge);
                    Add(_ => outside * face, CurveOutlineKind.Fade);
                    return;
                }
                Add(sample => outside * Math.Max(0, sample.Width) / 2, CurveOutlineKind.Edge);
                Add(sample => outside * (Math.Max(0, sample.Width) / 2 + Math.Max(0, area > 0 ? sample.FalloffLeft : sample.FalloffRight)), CurveOutlineKind.Fade);
                return;
            }
            if (curve.Profile == CurveProfile.Cliff)
            {
                Add(sample => Math.Max(Math.Max(0, sample.Width), face), CurveOutlineKind.Edge);
                Add(sample => Math.Max(Math.Max(0, sample.Width), face) + Math.Max(0, sample.FalloffLeft), CurveOutlineKind.Fade);
                return;
            }
            Add(sample => Math.Max(0, sample.Width) / 2, CurveOutlineKind.Edge);
            Add(sample => -Math.Max(0, sample.Width) / 2, CurveOutlineKind.Edge);
            Add(sample => Math.Max(0, sample.Width) / 2 + Math.Max(0, sample.FalloffLeft), CurveOutlineKind.Fade);
            Add(sample => -Math.Max(0, sample.Width) / 2 - Math.Max(0, sample.FalloffRight), CurveOutlineKind.Fade);
        }

        /// <summary>
        /// The line moved sideways at each sample by a distance, positive to the left of travel in
        /// the top view (x right, z down: the left of a direction (dx, dz) is (dz, -dx)).
        /// </summary>
        private static Vector2[] Offset(CurveSample[] line, Func<CurveSample, float> left, bool closed)
        {
            int count = line.Length;
            Vector2[] points = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                // a closed line's last sample is its first: the neighbours across the seam
                CurveSample before = i > 0 ? line[i - 1] : closed && count > 2 ? line[count - 2] : line[i];
                CurveSample after = i + 1 < count ? line[i + 1] : closed && count > 2 ? line[1] : line[i];
                Vector2 direction = new Vector2(after.X - before.X, after.Z - before.Z);
                float length = direction.Length();
                Vector2 normal = length > 1e-6f ? new Vector2(direction.Y, -direction.X) / length : Vector2.Zero;
                points[i] = new Vector2(line[i].X, line[i].Z) + normal * left(line[i]);
            }
            return points;
        }
    }
}
