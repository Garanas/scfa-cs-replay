using System.Collections.Immutable;

namespace FAForever.MapEditor.Project.Files.InMemory
{
    /// <summary>
    /// How a curve shapes the terrain across it (user story T-4).
    /// </summary>
    public enum CurveProfile
    {
        /// <summary>
        /// Raises a ridge along it by the points' heights, full over the width, fading over the falloff.
        /// </summary>
        Ridge,

        /// <summary>
        /// Lowers a valley along it by the points' heights.
        /// </summary>
        Valley,

        /// <summary>
        /// Flattens a road to the points' heights (absolute), full over the width, blending into
        /// the terrain over the falloff.
        /// </summary>
        Road,

        /// <summary>
        /// Raises the terrain on its left side (walking from the first point to the last, as seen
        /// in the top view) by the points' heights: a drop of about an ogrid at the curve, the high
        /// ground full over the width and fading back over the falloff.
        /// </summary>
        Cliff,
    }

    /// <summary>
    /// A point a curve passes through, in ogrids, with its height: the road's height there, or how
    /// much a ridge, valley or cliff rises or sinks there, in game units.
    /// </summary>
    public sealed record CurvePoint(float X, float Z, float Height);

    /// <summary>
    /// A curve through its points (a Catmull-Rom spline) that shapes the terrain along it.
    /// </summary>
    public sealed record HeightCurve
    {
        public ImmutableArray<CurvePoint> Points { get; init; } = [];

        public CurveProfile Profile { get; init; } = CurveProfile.Ridge;

        /// <summary>
        /// The width of the part at full height, in ogrids.
        /// </summary>
        public float Width { get; init; } = 8;

        /// <summary>
        /// How far beyond the width it fades out on each side, in ogrids.
        /// </summary>
        public float Falloff { get; init; } = 12;

        /// <summary>
        /// Whether the last point joins the first.
        /// </summary>
        public bool Closed { get; init; }

        /// <summary>
        /// The symmetry the curve is drawn by (user story F-12): its layer shapes the terrain along
        /// the curve's images in the other parts of the map too, so one curve makes a symmetric
        /// map's every river or ridge. Null for the curve alone.
        /// </summary>
        public SymmetrySetting? Symmetry { get; init; }

        public bool Equals(HeightCurve? other) =>
            other is not null && Profile == other.Profile && Width == other.Width && Falloff == other.Falloff && Closed == other.Closed
            && Equals(Symmetry, other.Symmetry) && Points.AsSpan().SequenceEqual(other.Points.AsSpan());

        public override int GetHashCode() => HashCode.Combine(Profile, Width, Falloff, Closed, Points.Length);
    }
}
